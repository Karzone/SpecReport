using Reqnroll;
using SpecReport.Capture;

namespace CaptureSampleTests.Support;

/// <summary>
/// The recommended explicit wiring from SPEC 3.4: a client built per scenario whose traffic
/// lands in that scenario's buffer. No ambient lookup is involved, so it is correct by
/// construction rather than by measurement.
/// </summary>
public sealed class ScenarioClient(ScenarioCapture capture) : IDisposable
{
    public HttpClient Http { get; } = new(
        new SpecReportHttpHandler(new StubTransport(), capture.Buffer))
    {
        BaseAddress = new Uri("https://probe.invalid")
    };

    public void Dispose() => Http.Dispose();
}

/// <summary>
/// Registers a screenshot provider the way a real UI suite would - a one-line delegate handing
/// over bytes from whatever driver the team uses. SpecReport never sees the driver (SPEC §3.5).
/// Here the "driver" is a tiny generated image, so the sample stays dependency-free.
/// </summary>
[Binding]
public sealed class ScreenshotWiring(ScenarioCapture capture)
{
    [Given("a screenshot provider is registered")]
    public void RegisterProvider() =>
        capture.UseScreenshotProvider(_ => Task.FromResult<byte[]?>(FakeScreenshot.Bytes()));

    /// <summary>
    /// The other half of SR-C7: an API suite registers nothing. This step exists to make that
    /// absence explicit and asserted, rather than merely untested.
    /// </summary>
    [Given("no screenshot provider is registered")]
    public static void RegisterNothing()
    {
    }
}

/// <summary>A few hundred bytes of valid JPEG. Enough to prove the pipeline attaches it.</summary>
internal static class FakeScreenshot
{
    private static readonly byte[] Jpeg = Convert.FromBase64String(
        "/9j/4AAQSkZJRgABAQEAYABgAAD/2wBDAAgGBgcGBQgHBwcJCQgKDBQNDAsLDBkSEw8UHRofHh0a" +
        "HBwgJC4nICIsIxwcKDcpLDAxNDQ0Hyc5PTgyPC4zNDL/wAALCAAIAAgBAREA/8QAHwAAAQUBAQEB" +
        "AQEAAAAAAAAAAAECAwQFBgcICQoL/8QAtRAAAgEDAwIEAwUFBAQAAAF9AQIDAAQRBRIhMUEGE1Fh" +
        "ByJxFDKBkaEII0KxwRVS0fAkM2JyggkKFhcYGRolJicoKSo0NTY3ODk6Q0RFRkdISUpTVFVWV1hZ" +
        "WmNkZWZnaGlqc3R1dnd4eXqDhIWGh4iJipKTlJWWl5iZmqKjpKWmp6ipqrKztLW2t7i5usLDxMXG" +
        "x8jJytLT1NXW19jZ2uHi4+Tl5ufo6erx8vP09fb3+Pn6/9oACAEBAAA/APn+iiiiv//Z");

    public static byte[] Bytes() => (byte[])Jpeg.Clone();
}

/// <summary>
/// Makes a call from a BeforeScenario hook - i.e. outside any step. SR-H2 requires this to be
/// bucketed as outside-steps rather than dropped or blamed on whichever step ran first.
/// </summary>
[Binding]
public sealed class OutOfStepTraffic(ScenarioClient client, ScenarioContext scenario)
{
    [BeforeScenario(Order = 100)]
    public async Task CallDuringSetup()
    {
        var slot = scenario.ScenarioInfo.Arguments.Count > 0
            ? scenario.ScenarioInfo.Arguments[0]?.ToString() ?? "none"
            : "none";

        var response = await client.Http.GetAsync($"/setup/{slot}");
        response.EnsureSuccessStatusCode();
    }

    [AfterScenario(Order = 100)]
    public void Cleanup() => client.Dispose();
}
