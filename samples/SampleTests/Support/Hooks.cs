using Reqnroll;

namespace SampleTests.Support;

/// <summary>
/// Hooks that produce the hook-envelope shapes the report has to render (SPEC 4.3).
/// The failing ones are the point: a scenario whose Before hook throws is FAILED with ZERO
/// failed steps, which is the most common real CI failure and the case that looks like a
/// renderer bug if hook rows aren't modelled.
/// </summary>
[Binding]
public sealed class Hooks
{
    [BeforeTestRun]
    public static void BeforeTestRun()
    {
        // Exists so the fixture carries testRunHookStarted/Finished envelopes.
    }

    [BeforeScenario(Order = 100)]
    public void BeforeEveryScenario()
    {
        // A passing hook: proves passing hook rows can be collapsed by default.
    }

    [BeforeScenario("fail-before-hook", Order = 200)]
    public void FailingBeforeHook()
        => throw new InvalidOperationException(
            "Simulated setup failure: could not seed the test database.");

    [AfterScenario("fail-after-hook")]
    public void FailingAfterHook()
        => throw new InvalidOperationException(
            "Simulated teardown failure: the browser session did not close cleanly.");

    [AfterScenario(Order = 1000)]
    public void AfterEveryScenario()
    {
        // Runs even when a Before hook failed - see docs/reqnroll-api-notes.md 4.
    }

    [AfterTestRun]
    public static void AfterTestRun()
    {
    }
}
