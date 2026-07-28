namespace SpecReport.Core.Model;

public enum StepStatus
{
    Unknown,
    Passed,
    Skipped,
    Pending,
    Undefined,
    Ambiguous,
    Failed
}

/// <summary>Where a status came from. Inferred statuses are rendered distinctly so a reader can
/// never mistake a parser guess for something the run actually reported (SPEC §4.4).</summary>
public enum StatusSource
{
    Reported,
    Inferred
}

public sealed class ScenarioModel
{
    /// <summary>
    /// Stable across runs. Derived from feature URI + rule + name + example-row discriminator,
    /// NOT from pickle id: a pickle hash changes when the scenario's own text changes, so a
    /// typo fix would read as "deleted and added" and blow a hole in the multi-run matrix
    /// (SPEC §6.2).
    /// </summary>
    public required ScenarioIdentity Identity { get; init; }

    public string Name { get; init; } = string.Empty;
    public string Keyword { get; init; } = "Scenario";
    public string? Description { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>Example-row values for a Scenario Outline, in column order. Empty for a plain
    /// scenario.</summary>
    public IReadOnlyDictionary<string, string> ExampleValues { get; init; } =
        new Dictionary<string, string>();

    /// <summary>
    /// Every execution of this scenario in this run, oldest first. More than one means it was
    /// retried. Counts and status come from the LAST attempt, so a retried scenario is one
    /// scenario, not three (SPEC §4.5).
    /// </summary>
    public IReadOnlyList<ScenarioAttempt> Attempts { get; init; } = [];

    public ScenarioAttempt? Outcome => Attempts.Count > 0 ? Attempts[^1] : null;

    public StepStatus Status => Outcome?.Status ?? StepStatus.Unknown;

    /// <summary>Failed on one attempt and passed on another, within this single run. A stronger
    /// flakiness signal than cross-run flipping, and free once attempts are modelled.</summary>
    public bool IsFlakyWithinRun =>
        Attempts.Count > 1 &&
        Attempts.Select(a => a.Status).Distinct().Count() > 1;
}

/// <summary>
/// One execution of a scenario. Separate from <see cref="ScenarioModel"/> so retries nest
/// rather than duplicating the scenario.
/// </summary>
public sealed class ScenarioAttempt
{
    public int AttemptNumber { get; init; }
    public DateTimeOffset? StartedAt { get; init; }
    public DateTimeOffset? FinishedAt { get; init; }

    public IReadOnlyList<StepModel> Steps { get; init; } = [];

    /// <summary>
    /// Worst status across all steps INCLUDING hook steps - a scenario whose BeforeScenario hook
    /// threw is failed even though every Gherkin step is skipped, which is the most common real
    /// CI failure (SPEC §4.3).
    /// </summary>
    public StepStatus Status
    {
        get
        {
            var worst = StepStatus.Unknown;
            var sawPass = false;

            foreach (var step in Steps)
            {
                switch (step.Status)
                {
                    case StepStatus.Failed:
                        return StepStatus.Failed;
                    case StepStatus.Ambiguous:
                    case StepStatus.Undefined:
                    case StepStatus.Pending:
                        if (worst is StepStatus.Unknown or StepStatus.Skipped) worst = step.Status;
                        break;
                    case StepStatus.Skipped:
                        if (worst == StepStatus.Unknown) worst = StepStatus.Skipped;
                        break;
                    case StepStatus.Passed:
                        sawPass = true;
                        break;
                }
            }

            if (worst != StepStatus.Unknown) return worst;
            return sawPass ? StepStatus.Passed : StepStatus.Unknown;
        }
    }

    public TimeSpan Duration =>
        TimeSpan.FromTicks(Steps.Sum(s => s.Duration.Ticks));

    /// <summary>The first failure, which is what the collapsed scenario row shows.</summary>
    public StepModel? FirstFailure => Steps.FirstOrDefault(s => s.Status == StepStatus.Failed);

    /// <summary>Evidence captured outside any step - BeforeScenario/AfterScenario hooks,
    /// background polling. Rendered at scenario level rather than blamed on a step (SPEC §3.4).</summary>
    public IReadOnlyList<Evidence> OutsideStepEvidence { get; init; } = [];
}

/// <summary>
/// Identity that survives across runs. Value-equal by construction so it can key a dictionary
/// in the multi-run matrix.
/// </summary>
public sealed record ScenarioIdentity(
    string FeatureUri,
    string? RuleName,
    string Name,
    string? ExampleRowDiscriminator)
{
    public override string ToString() =>
        $"{FeatureUri}::{RuleName ?? "-"}::{Name}::{ExampleRowDiscriminator ?? "-"}";
}
