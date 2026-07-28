using Reqnroll;

namespace M1.HttpAttribution.Support;

/// <summary>
/// Seeds every candidate mechanism at scenario start and reports the verdict at run end.
/// </summary>
[Binding]
public sealed class SpikeHooks(ScenarioContext scenario, ScenarioState state)
{
    /// <summary>
    /// Deliberately an ASYNC hook. That is the realistic case and the suspected killer for
    /// AsyncLocal: assigning AsyncLocal.Value inside an async method mutates a copy-on-write
    /// ExecutionContext that is discarded when the hook returns.
    /// </summary>
    [BeforeScenario]
    public async Task SeedAmbientState()
    {
        await Task.Yield();

        state.ScenarioId = ScenarioId(scenario);
        state.Client = new HttpClient(new SpikeHandler(state.ScenarioId))
        {
            BaseAddress = new Uri("https://probe.invalid")
        };

        AmbientProbe.Seed(state.ScenarioId);
    }

    /// <summary>
    /// Deliberately SYNCHRONOUS, and ordered after the async seeder so it observes the same
    /// scenario. If Reqnroll invokes sync hooks inline, this AsyncLocal assignment may reach
    /// the steps where the async one cannot.
    /// </summary>
    [BeforeScenario(Order = 500)]
    public void SeedFromSynchronousHook()
        => AmbientProbe.SeedFromSyncHook(ScenarioId(scenario));

    [AfterScenario]
    public void Dispose() => state.Client?.Dispose();

    [AfterTestRun]
    public static void ReportVerdict()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "probe-results.txt");
        File.WriteAllText(path, ProbeLog.Summarise() + Environment.NewLine);
    }

    /// <summary>Scenario Outline rows share a title, so the row's parameter disambiguates.</summary>
    public static string ScenarioId(ScenarioContext ctx)
    {
        var slot = ctx.ScenarioInfo.Arguments.Count > 0
            ? ctx.ScenarioInfo.Arguments[0]?.ToString()
            : null;

        return slot ?? ctx.ScenarioInfo.Title.Replace(' ', '-');
    }
}

/// <summary>Per-scenario state, resolved from Reqnroll's scenario container.</summary>
public sealed class ScenarioState
{
    public string ScenarioId { get; set; } = "?";
    public HttpClient? Client { get; set; }
}
