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
