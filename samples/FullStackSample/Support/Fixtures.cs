using FullStackSample.App;
using System.Collections.Generic;
using Microsoft.Playwright;
using Reqnroll;
using RestSharp;
using SpecReport.Capture;

namespace FullStackSample.Support;

/// <summary>
/// The demo shop, started once for the whole run. Scenarios share it, which is realistic - a
/// suite does not restart its environment between tests.
/// </summary>
[Binding]
public static class ShopHost
{
    private static DemoShop? _shop;

    public static string BaseUrl =>
        _shop?.BaseUrl ?? throw new InvalidOperationException("The demo shop is not running.");

    [BeforeTestRun]
    public static async Task StartAsync() => _shop = await DemoShop.StartAsync();

    [AfterTestRun]
    public static async Task StopAsync()
    {
        if (_shop is not null) await _shop.DisposeAsync();
    }
}

/// <summary>
/// Playwright browser and page, one per scenario, plus the screenshot provider SpecReport
/// needs. This is the whole UI-side integration: one delegate.
/// </summary>
public sealed class BrowserSession(ScenarioCapture capture, ScenarioSession session) : IAsyncDisposable
{
    private IPlaywright? _playwright;
    private IBrowser? _browser;

    public IPage Page { get; private set; } = null!;

    public async Task StartAsync()
    {
        _playwright = await Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync(new() { Headless = true });

        var context = await _browser.NewContextAsync(new()
        {
            ViewportSize = new ViewportSize { Width = 1100, Height = 700 },
            // The browser shares the scenario's session with the API client, so a basket
            // filled through the API is the same basket the storefront renders.
            ExtraHTTPHeaders = new Dictionary<string, string>
            {
                [DemoShop.SessionHeader] = session.Id
            }
        });

        Page = await context.NewPageAsync();

        // SpecReport never references Playwright - the consumer hands it bytes. JPEG at a
        // modest quality keeps the attachment inside the size budget, since Reqnroll inlines
        // every byte into the NDJSON.
        capture.UseScreenshotProvider(async _ =>
            await Page.ScreenshotAsync(new() { Type = ScreenshotType.Jpeg, Quality = 45 }));
    }

    public async ValueTask DisposeAsync()
    {
        if (_browser is not null) await _browser.CloseAsync();
        _playwright?.Dispose();
    }
}

/// <summary>
/// Both HTTP clients, routed through SpecReport's handler.
///
/// The point of having two: `HttpClient` and `RestSharp` are different libraries with different
/// APIs, and capture works identically for both because RestSharp - like Refit, Flurl, and
/// typed clients - is built on HttpMessageHandler underneath.
/// </summary>
public sealed class ApiClients(ScenarioCapture capture, ScenarioSession session) : IDisposable
{
    private HttpClient? _http;
    private RestClient? _rest;

    public HttpClient Http => _http ??= Create();

    public RestClient Rest => _rest ??= new RestClient(
        Create(), disposeHttpClient: true, configureRestClient: o => o.ThrowOnAnyError = false);

    private HttpClient Create()
    {
        var client = new HttpClient(capture.CreateHandler())
        {
            BaseAddress = new Uri(ShopHost.BaseUrl)
        };

        client.DefaultRequestHeaders.Add(DemoShop.SessionHeader, session.Id);
        return client;
    }

    public void Dispose()
    {
        _http?.Dispose();
        _rest?.Dispose();
    }
}

/// <summary>
/// Identifies this scenario to the shop, so parallel scenarios get their own basket. Shared by
/// the API client and the browser.
/// </summary>
public sealed class ScenarioSession
{
    public string Id { get; } = Guid.NewGuid().ToString("N")[..12];
}

/// <summary>Scenario lifecycle. The browser only starts for scenarios that need one.</summary>
[Binding]
public sealed class ScenarioLifecycle(BrowserSession browser)
{
    [BeforeScenario("@ui")]
    public async Task StartBrowserAsync() => await browser.StartAsync();

    [AfterScenario("@ui")]
    public async Task StopBrowserAsync() => await browser.DisposeAsync();
}
