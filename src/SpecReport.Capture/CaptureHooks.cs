using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Reqnroll;

namespace SpecReport.Capture;

/// <summary>
/// Turns buffered exchanges into step-attributed attachments.
///
/// Reqnroll stamps every attachment with the CURRENT step id automatically (verified - see
/// docs/reqnroll-api-notes.md 3), so draining inside [AfterStep] is all the attribution
/// required. No id juggling, no correlation table.
/// </summary>
[Binding]
public sealed class CaptureHooks
{
    private readonly ScenarioCapture _capture;
    private readonly IReqnrollOutputHelper _output;
    private readonly ScenarioContext _scenario;

    /// <summary>
    /// ScenarioStepContext is deliberately NOT constructor-injected: BoDi tries to construct it
    /// and fails on StepInfo's primitive dependencies. Reach it through ScenarioContext instead.
    /// </summary>
    public CaptureHooks(ScenarioCapture capture, IReqnrollOutputHelper output,
                        ScenarioContext scenario)
    {
        _capture = capture;
        _output = output;
        _scenario = scenario;
    }

    /// <summary>
    /// MUST STAY SYNCHRONOUS. CaptureScope binds an AsyncLocal, and an async hook's assignment
    /// is discarded when the hook returns - measured, not assumed (FINDINGS.md). Making this
    /// `async Task` would silently disable ambient capture: no exception, no failing test, just
    /// an empty HTTP panel. CaptureScopeGuardTests exists to catch that refactor.
    /// </summary>
    [BeforeScenario(Order = 0)]
    public void BeginScenario() => CaptureScope.Enter(_capture.Buffer);

    /// <summary>
    /// Opens the step window and records which step it is. Exchanges captured from here until
    /// the matching [AfterStep] carry this identity.
    ///
    /// We track the index ourselves because Reqnroll's own step id is unusable for this:
    /// attachments added from an [AfterStep] hook are stamped with the AFTER_TEST_STEP hook's
    /// id, not the Gherkin step's (measured - see docs/reqnroll-api-notes.md 10).
    /// </summary>
    [BeforeStep]
    public void BeginStep()
    {
        _capture.Buffer.EnterStep(_capture.NextStepIndex(), CurrentStepText());
    }

    private string? CurrentStepText() => SafeStepContext()?.StepInfo?.Text;

    /// <summary>
    /// ScenarioStepContext throws outside a step and can throw on a disposed container. Step
    /// text and failure detection are both reporting concerns - neither is worth failing a
    /// consumer's run over.
    /// </summary>
    private ScenarioStepContext? SafeStepContext()
    {
        try
        {
            return _scenario.StepContext;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Drains what this step captured. Reqnroll stamps the attachment with the current step id,
    /// so this is the whole of attribution.
    ///
    /// Only STEP-scoped exchanges drain here. Anything captured outside a step waits for
    /// scenario teardown - draining it now would attach it to whichever step happened to run
    /// next, which is precisely the misattribution SR-H2 forbids.
    /// </summary>
    [AfterStep]
    public async Task AttachStepEvidence()
    {
        Attach(_capture.Buffer.Drain(ExchangeScope.Step));
        await AttachScreenshotAsync().ConfigureAwait(false);
        _capture.Buffer.LeaveStep();
    }

    /// <summary>
    /// Captures a screenshot if the consumer registered a provider and the mode calls for one.
    ///
    /// Async is fine HERE - unlike <see cref="BeginScenario"/>, nothing about this depends on
    /// AsyncLocal flow. The buffer is reached through the injected ScenarioCapture instance.
    /// </summary>
    private async Task AttachScreenshotAsync()
    {
        var options = _capture.Options.Screenshots;
        if (options.Mode == ScreenshotMode.Off) return;

        // StepError is the unambiguous signal: the exception THIS step threw. Status was tried
        // first and fired on a passing step - measured, not assumed - so precision matters more
        // than it looks.
        var stepContext = SafeStepContext();
        var failed = stepContext?.StepError is not null;

        if (options.Mode == ScreenshotMode.OnFailure && !failed) return;

        var result = await ScreenshotResult
            .TryCaptureAsync(_capture.ScreenshotProvider, options, CancellationToken.None)
            .ConfigureAwait(false);

        if (result.Bytes is { } bytes)
        {
            // The step ordinal goes in the FILE NAME, which Reqnroll copies into the attachment
            // envelope verbatim.
            //
            // It cannot go anywhere else: an attachment added from [AfterStep] is stamped with
            // the AFTER_TEST_STEP hook's id, not the step's (api-notes §10), and unlike an
            // http/1 payload a JPEG has nowhere to carry an index. Without this the screenshot
            // from a failing step renders against step 1 - measured, not theorised.
            _output.AddAttachment(WriteBytes(bytes, options.FileExtension));
            return;
        }

        // "No provider" is the normal state for an API suite - silence is correct. Anything
        // else is worth a line in the report, because a missing screenshot on a failed step is
        // otherwise indistinguishable from one nobody asked for.
        if (result.Skipped is { } why && why != "no provider")
            _output.WriteLine($"[SpecReport] {why}");
    }

    /// <summary>
    /// Sweeps up anything captured outside a step - Before/AfterScenario hooks, background
    /// polling. It stays marked outside-steps so the report shows it at scenario level rather
    /// than blaming an unrelated step (SPEC 3.4). DrainAll rather than a scope filter, because
    /// nothing should ever be silently discarded at teardown.
    /// </summary>
    [AfterScenario(Order = 10000)]
    public void AttachRemainingExchanges()
    {
        Attach(_capture.Buffer.DrainAll());
        CaptureScope.Exit();
    }

    private void Attach(IReadOnlyList<HttpExchange> exchanges)
    {
        foreach (var exchange in exchanges)
            _output.AddAttachment(WriteToTempFile(exchange));
    }

    /// <summary>
    /// AddAttachment takes a PATH - Reqnroll reads the file and inlines it base64 into the
    /// NDJSON. The `.json` extension matters: media type is inferred from it, and the report
    /// dispatches on media type plus the in-payload discriminator.
    /// </summary>
    private string WriteToTempFile(HttpExchange exchange)
    {
        var path = Path.Combine(ScratchDirectory(), "http-" + exchange.Sequence.ToString("D4") + ".json");
        File.WriteAllText(path, exchange.ToJson());
        return path;
    }

    /// <summary>
    /// Writes a screenshot whose file name carries the Gherkin step ordinal, in the form
    /// <c>specreport-step-0004-…</c>. <see cref="StepOrdinalPrefix"/> documents the contract;
    /// SpecReport.Core parses it back when placing evidence.
    /// </summary>
    private string WriteBytes(byte[] bytes, string extension)
    {
        var ordinal = _capture.CurrentStepIndex.ToString("D4");
        var unique = System.Guid.NewGuid().ToString("N").Substring(0, 6);

        var path = Path.Combine(
            ScratchDirectory(), $"{StepOrdinalPrefix}{ordinal}-{unique}{extension}");

        File.WriteAllBytes(path, bytes);
        return path;
    }

    /// <summary>
    /// File-name prefix that carries a step ordinal through Reqnroll's attachment pipeline.
    /// Shared with the renderer, which parses it - changing it on one side alone silently
    /// misplaces every screenshot.
    /// </summary>
    public const string StepOrdinalPrefix = "specreport-step-";

    private string ScratchDirectory()
    {
        var dir = Path.Combine(Path.GetTempPath(), "specreport-capture", _capture.Id);
        Directory.CreateDirectory(dir);
        return dir;
    }
}

/// <summary>
/// Per-scenario capture state. Registered in Reqnroll's scenario container by
/// <see cref="SpecReportPlugin"/>, so bindings and hooks receive the same instance for the
/// lifetime of one scenario - which is what makes parallel scenarios independent.
/// </summary>
public sealed class ScenarioCapture
{
    public ExchangeBuffer Buffer { get; } = new ExchangeBuffer();

    /// <summary>Unique per scenario instance; used to keep temp files from colliding between
    /// concurrently running scenarios.</summary>
    public string Id { get; } = System.Guid.NewGuid().ToString("N");

    public CaptureOptions Options { get; private set; } = CaptureOptions.Default;

    internal ScreenshotProvider? ScreenshotProvider { get; private set; }

    /// <summary>
    /// Registers how to take a screenshot. Call from a scenario hook, passing whatever your
    /// driver offers - SpecReport never references one itself (SPEC §3.5).
    ///
    /// Not calling this is a perfectly normal state: an API suite takes no screenshots, and
    /// that produces no error and no warning.
    /// </summary>
    public void UseScreenshotProvider(ScreenshotProvider provider) =>
        ScreenshotProvider = provider ?? throw new System.ArgumentNullException(nameof(provider));

    /// <summary>Overrides capture settings for this scenario - redaction lists, body caps,
    /// screenshot mode.</summary>
    public void UseOptions(CaptureOptions options) =>
        Options = options ?? throw new System.ArgumentNullException(nameof(options));

    private int _stepIndex = -1;

    /// <summary>Ordinal of the step currently executing, or -1 outside a step.</summary>
    public int CurrentStepIndex => _stepIndex;

    /// <summary>
    /// Zero-based ordinal of the next step in this scenario. Steps within a scenario execute
    /// sequentially, so a plain counter is sufficient - the concurrency in play is between
    /// scenarios, and each has its own ScenarioCapture.
    /// </summary>
    public int NextStepIndex() => ++_stepIndex;

    /// <summary>
    /// Convenience for the recommended explicit wiring: build a client per scenario whose
    /// traffic lands in this scenario's buffer, with no ambient lookup involved.
    /// </summary>
    public System.Net.Http.HttpClient CreateClient(
        System.Net.Http.HttpMessageHandler? innerHandler = null,
        CaptureOptions? options = null)
    {
        var handler = innerHandler is null
            ? new SpecReportHttpHandler(Buffer, options)
            : new SpecReportHttpHandler(innerHandler, Buffer, options);

        return new System.Net.Http.HttpClient(handler);
    }

    /// <summary>
    /// A capturing handler ready to use.
    ///
    /// It comes with a real inner handler already attached, so `new HttpClient(CreateHandler())`
    /// works - the obvious thing a consumer writes. Returning a bare DelegatingHandler instead
    /// fails at the first request with "The inner handler has not been assigned", which says
    /// nothing about what to do next.
    ///
    /// Pass <paramref name="innerHandler"/> to wrap an existing pipeline, or assign
    /// <c>InnerHandler</c> afterwards - either is fine before the first request.
    /// </summary>
    public System.Net.Http.DelegatingHandler CreateHandler(
        System.Net.Http.HttpMessageHandler? innerHandler = null,
        CaptureOptions? options = null)
        => new SpecReportHttpHandler(
            innerHandler ?? new System.Net.Http.HttpClientHandler(), Buffer, options ?? Options);
}
