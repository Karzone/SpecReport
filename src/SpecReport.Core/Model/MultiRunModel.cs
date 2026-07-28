namespace SpecReport.Core.Model;

/// <summary>
/// Status of one scenario in one run.
///
/// <see cref="NotRun"/> is a THIRD state, distinct from Skipped. A scenario absent from a run -
/// because it was added later, filtered out by tags, or lost to a missing shard - is not the
/// same as one the run chose to skip. Conflating them makes the matrix lie and makes the flaky
/// list fire on scenarios that simply did not exist yet (SPEC §6.3).
/// </summary>
public enum CellStatus
{
    NotRun,
    Passed,
    Failed,
    Skipped,
    Pending,
    Undefined,
    Ambiguous,
    Unknown
}

/// <summary>A set of runs compared side by side.</summary>
public sealed class MultiRunModel
{
    /// <summary>Oldest first, ordered by <c>testRunStarted.timestamp</c> - never by filename or
    /// the order files were given on the command line (SPEC §6.1).</summary>
    public IReadOnlyList<RunSummary> Runs { get; init; } = [];

    /// <summary>One row per scenario identity, with a cell per run.</summary>
    public IReadOnlyList<ScenarioSeries> Series { get; init; } = [];

    /// <summary>
    /// Scenarios with more than one distinct EXECUTED outcome across the set, or which flipped
    /// within a single run. Ranked by flip count.
    /// </summary>
    public IEnumerable<ScenarioSeries> Flaky =>
        Series.Where(s => s.IsFlaky).OrderByDescending(s => s.FlipCount).ThenBy(s => s.Name);
}

public sealed class RunSummary
{
    public required string Label { get; init; }
    public DateTimeOffset? StartedAt { get; init; }
    public TimeSpan? Duration { get; init; }

    /// <summary>The files this run was assembled from. More than one means it was sharded.</summary>
    public IReadOnlyList<string> SourceFiles { get; init; } = [];

    public IReadOnlyDictionary<CellStatus, int> Counts { get; init; } =
        new Dictionary<CellStatus, int>();

    public int TotalScenarios { get; init; }
    public double PassRate { get; init; }

    /// <summary>The merged run itself, so the report can drill into a full timeline.</summary>
    public required RunModel Run { get; init; }

    public bool WasSharded => SourceFiles.Count > 1;
}

public sealed class ScenarioSeries
{
    public required ScenarioIdentity Identity { get; init; }
    public required string Name { get; init; }
    public string FeatureUri { get; init; } = string.Empty;
    public string? RuleName { get; init; }

    /// <summary>One cell per run, in run order.</summary>
    public IReadOnlyList<Cell> Cells { get; init; } = [];

    /// <summary>Runs where this scenario actually executed.</summary>
    public IEnumerable<Cell> Executed => Cells.Where(c => c.Status != CellStatus.NotRun);

    /// <summary>
    /// Number of times the outcome changed between consecutive EXECUTED runs. Gaps are skipped
    /// rather than counted as changes - a scenario that did not run is not evidence of anything.
    /// </summary>
    public int FlipCount
    {
        get
        {
            var flips = 0;
            CellStatus? previous = null;

            foreach (var cell in Executed)
            {
                if (previous is { } p && p != cell.Status) flips++;
                previous = cell.Status;
            }

            return flips;
        }
    }

    /// <summary>
    /// Flaky means inconsistent across the runs supplied, or inconsistent within a single run.
    /// A deliberately narrow, honest definition - it is not a statistical claim about the
    /// scenario in general (SPEC §6.3).
    /// </summary>
    public bool IsFlaky => FlipCount > 0 || Cells.Any(c => c.FlakyWithinRun);

    public bool EverFailed => Cells.Any(c => c.Status == CellStatus.Failed);
}

public sealed class Cell
{
    public CellStatus Status { get; init; } = CellStatus.NotRun;
    public TimeSpan Duration { get; init; }

    /// <summary>Failed then passed inside this one run - a stronger flakiness signal than
    /// cross-run flipping, and free once retries are modelled (SPEC §4.5).</summary>
    public bool FlakyWithinRun { get; init; }

    public int AttemptCount { get; init; }
}
