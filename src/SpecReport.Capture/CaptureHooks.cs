using System.Collections.Generic;
using System.IO;
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

    private string? CurrentStepText()
    {
        try
        {
            return _scenario.StepContext?.StepInfo?.Text;
        }
        catch
        {
            // Step text is a nicety for the report; never fail a consumer's run over it.
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
    public void AttachStepExchanges()
    {
        Attach(_capture.Buffer.Drain(ExchangeScope.Step));
        _capture.Buffer.LeaveStep();
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
        var dir = Path.Combine(Path.GetTempPath(), "specreport-capture", _capture.Id);
        Directory.CreateDirectory(dir);

        var path = Path.Combine(dir, "http-" + exchange.Sequence.ToString("D4") + ".json");
        File.WriteAllText(path, exchange.ToJson());
        return path;
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

    private int _stepIndex = -1;

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

    /// <summary>Wraps an existing handler chain so factory-configured pipelines keep working.</summary>
    public System.Net.Http.DelegatingHandler CreateHandler(CaptureOptions? options = null)
        => new SpecReportHttpHandler(Buffer, options);
}
