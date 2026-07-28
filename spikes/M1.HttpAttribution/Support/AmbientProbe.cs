using Reqnroll;

namespace M1.HttpAttribution.Support;

/// <summary>
/// M1 SPIKE. The question this exists to answer, empirically rather than by reasoning:
///
///   A DelegatingHandler lives in the SUT's DI world, not Reqnroll's. When it captures an HTTP
///   exchange, how does it find the CURRENT SCENARIO's buffer - correctly, under parallel
///   execution with async steps?
///
/// Four candidate mechanisms are seeded per scenario and read back from inside the handler on
/// every request. Each records whether it saw the right scenario, the wrong one, or nothing.
/// Getting the WRONG one is the dangerous outcome: it silently attributes one scenario's HTTP
/// traffic to another, which is worse than capturing nothing.
/// </summary>
public static class AmbientProbe
{
    /// <summary>Candidate 1: AsyncLocal assigned inside an async [BeforeScenario] hook.
    /// Suspected broken - assigning AsyncLocal.Value inside an async method mutates that
    /// method's copy-on-write ExecutionContext, which is discarded when the hook returns.</summary>
    private static readonly AsyncLocal<string?> AsyncLocalDirect = new();

    /// <summary>Candidate 2: AsyncLocal holding a MUTABLE HOLDER, where the holder reference is
    /// planted once and only its contents change per scenario. Sidesteps the copy-on-write
    /// problem for the reference - but the holder is then shared, so under parallelism it may
    /// see another scenario's value. That is exactly what we need to find out.</summary>
    private static readonly AsyncLocal<Holder> AsyncLocalHolder = new();

    /// <summary>Candidate 3: [ThreadStatic]. Suspected broken the moment an async step resumes
    /// on a different thread-pool thread.</summary>
    [ThreadStatic] private static string? _threadStatic;

    /// <summary>Candidate 5: AsyncLocal assigned from a SYNCHRONOUS hook. A sync method has no
    /// async state machine of its own, so if Reqnroll invokes it inline the assignment could
    /// land on a context the steps inherit. Whether Reqnroll's binding invoker introduces an
    /// async boundary anyway is precisely what we cannot know without measuring - and it
    /// decides whether ambient mode is viable at all for factory-built clients.</summary>
    private static readonly AsyncLocal<string?> AsyncLocalFromSyncHook = new();

    public static void SeedFromSyncHook(string scenarioId) => AsyncLocalFromSyncHook.Value = scenarioId;

    /// <summary>Candidate 4: an explicit per-scenario object handed to the handler at
    /// construction - no ambient lookup at all. Expected to be correct by construction; the
    /// question is whether it is usable (it requires the client to be built per scenario).</summary>
    public sealed class Holder
    {
        public string? ScenarioId;
    }

    public static void Seed(string scenarioId)
    {
        AsyncLocalDirect.Value = scenarioId;

        // Plant a fresh holder. If AsyncLocal reference assignment doesn't survive the hook,
        // reads will see null; if it survives but leaks across scenarios, reads will see
        // another scenario's id.
        AsyncLocalHolder.Value = new Holder { ScenarioId = scenarioId };

        _threadStatic = scenarioId;
    }

    public static Reading Read() => new(
        AsyncLocalDirect: AsyncLocalDirect.Value,
        AsyncLocalHolder: AsyncLocalHolder.Value?.ScenarioId,
        AsyncLocalSyncHook: AsyncLocalFromSyncHook.Value,
        ThreadStatic: _threadStatic,
        ThreadId: Environment.CurrentManagedThreadId);

    public readonly record struct Reading(
        string? AsyncLocalDirect,
        string? AsyncLocalHolder,
        string? AsyncLocalSyncHook,
        string? ThreadStatic,
        int ThreadId);

    /// <summary>Verdict for one mechanism on one observation.</summary>
    public static string Verdict(string? observed, string expected) => observed switch
    {
        null => "ABSENT",
        _ when observed == expected => "CORRECT",
        _ => "WRONG"          // the dangerous one: cross-scenario contamination
    };
}

/// <summary>
/// Records every probe observation across the whole run so the spike can report a verdict
/// per mechanism rather than per scenario.
/// </summary>
public static class ProbeLog
{
    private static readonly object Gate = new();
    private static readonly List<Observation> Entries = [];

    public sealed record Observation(
        string ExpectedScenario,
        string ExpectedStep,
        string AsyncLocalDirect,
        string AsyncLocalHolder,
        string AsyncLocalSyncHook,
        string ThreadStatic,
        string Explicit,
        int ThreadId);

    public static void Add(Observation o)
    {
        lock (Gate) Entries.Add(o);
    }

    public static IReadOnlyList<Observation> Snapshot()
    {
        lock (Gate) return [.. Entries];
    }

    public static string Summarise()
    {
        var all = Snapshot();
        if (all.Count == 0) return "no observations recorded";

        static string Tally(IReadOnlyList<Observation> obs, Func<Observation, string> pick)
        {
            var g = obs.GroupBy(pick).OrderBy(x => x.Key)
                       .Select(x => $"{x.Key}={x.Count()}");
            return string.Join(" ", g);
        }

        var threads = all.Select(o => o.ThreadId).Distinct().Count();

        return $"""
                observations : {all.Count} across {threads} distinct threads

                  CORRECT = attributed to the right scenario
                  WRONG   = attributed to ANOTHER scenario (silent corruption - the worst outcome)
                  ABSENT  = no value at all (useless, but at least honest)

                AsyncLocal(async hook) : {Tally(all, o => o.AsyncLocalDirect)}
                AsyncLocal(holder)     : {Tally(all, o => o.AsyncLocalHolder)}
                AsyncLocal(sync hook)  : {Tally(all, o => o.AsyncLocalSyncHook)}
                ThreadStatic           : {Tally(all, o => o.ThreadStatic)}
                Explicit ctor          : {Tally(all, o => o.Explicit)}
                """;
    }
}
