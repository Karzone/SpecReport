using SpecReport.Core.Model;

namespace SpecReport.Core.Parsing;

/// <summary>One logical run: a label and the files it is assembled from.</summary>
public sealed record RunGroup(string Label, IReadOnlyList<string> Files);

public static class MultiRunBuilder
{
    /// <summary>
    /// Parses and merges each group, orders the runs, and builds the scenario x run matrix.
    /// </summary>
    public static MultiRunModel Build(IReadOnlyList<RunGroup> groups, AttachmentSpool spool)
    {
        var summaries = new List<RunSummary>();

        foreach (var group in groups)
        {
            var shards = group.Files.Select(f => RunParser.Parse(f, spool)).ToList();
            if (shards.Count == 0) continue;

            var merged = RunMerger.Merge(shards);
            var scenarios = merged.AllScenarios.ToList();

            var counts = scenarios
                .GroupBy(s => ToCellStatus(s.Status))
                .ToDictionary(g => g.Key, g => g.Count());

            summaries.Add(new RunSummary
            {
                Label = group.Label,
                StartedAt = merged.StartedAt,
                Duration = merged.Duration,
                SourceFiles = group.Files,
                Counts = counts,
                TotalScenarios = scenarios.Count,
                PassRate = scenarios.Count == 0
                    ? 0
                    : Math.Round(counts.GetValueOrDefault(CellStatus.Passed) * 100.0 / scenarios.Count, 1),
                Run = merged
            });
        }

        // Oldest first, by the run's own clock. Filename order would put "run-10" before
        // "run-2", and argument order is whatever the shell's glob happened to produce
        // (SPEC §6.1). Runs with no timestamp sort last, keeping their relative order.
        var ordered = summaries
            .Select((summary, index) => (summary, index))
            .OrderBy(x => x.summary.StartedAt ?? DateTimeOffset.MaxValue)
            .ThenBy(x => x.index)
            .Select(x => x.summary)
            .ToList();

        return new MultiRunModel
        {
            Runs = ordered,
            Series = BuildSeries(ordered)
        };
    }

    private static List<ScenarioSeries> BuildSeries(IReadOnlyList<RunSummary> runs)
    {
        // Identity is what makes a row: derived from feature URI + rule + name + example-row
        // discriminator, so it survives across runs where pickle ids do not (SPEC §6.2).
        var byIdentity = new Dictionary<ScenarioIdentity, ScenarioModel[]>();
        var order = new List<ScenarioIdentity>();

        for (var i = 0; i < runs.Count; i++)
        {
            foreach (var scenario in runs[i].Run.AllScenarios)
            {
                if (!byIdentity.TryGetValue(scenario.Identity, out var slots))
                {
                    byIdentity[scenario.Identity] = slots = new ScenarioModel[runs.Count];
                    order.Add(scenario.Identity);
                }

                slots[i] = scenario;
            }
        }

        var series = new List<ScenarioSeries>();

        foreach (var identity in order)
        {
            var slots = byIdentity[identity];
            var present = slots.FirstOrDefault(s => s is not null);
            if (present is null) continue;

            series.Add(new ScenarioSeries
            {
                Identity = identity,
                Name = present.Name,
                FeatureUri = identity.FeatureUri,
                RuleName = identity.RuleName,
                Cells = slots.Select(ToCell).ToList()
            });
        }

        return series
            .OrderBy(s => s.FeatureUri, StringComparer.Ordinal)
            .ThenBy(s => s.Name, StringComparer.Ordinal)
            .ToList();
    }

    private static Cell ToCell(ScenarioModel? scenario) => scenario is null
        // Absent from this run entirely - not the same as skipped, and excluded from flakiness.
        ? new Cell { Status = CellStatus.NotRun }
        : new Cell
        {
            Status = ToCellStatus(scenario.Status),
            Duration = scenario.Outcome?.Duration ?? TimeSpan.Zero,
            FlakyWithinRun = scenario.IsFlakyWithinRun,
            AttemptCount = scenario.Attempts.Count
        };

    private static CellStatus ToCellStatus(StepStatus status) => status switch
    {
        StepStatus.Passed => CellStatus.Passed,
        StepStatus.Failed => CellStatus.Failed,
        StepStatus.Skipped => CellStatus.Skipped,
        StepStatus.Pending => CellStatus.Pending,
        StepStatus.Undefined => CellStatus.Undefined,
        StepStatus.Ambiguous => CellStatus.Ambiguous,
        _ => CellStatus.Unknown
    };
}
