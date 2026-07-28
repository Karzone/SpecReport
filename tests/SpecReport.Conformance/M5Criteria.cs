using SpecReport.Core.Model;
using SpecReport.Core.Parsing;
using SpecReport.Core.Rendering;
using Xunit;

namespace SpecReport.Conformance;

/// <summary>
/// Evidence for milestone M5: multi-run reporting.
///
/// The fixtures model the topology that actually breaks naive implementations - one logical run
/// split across two shard files, followed by a later single-file run that shares most of its
/// scenarios, flips two outcomes, adds one that did not exist before, and retries one.
/// </summary>
public sealed class M5Criteria : IDisposable
{
    private readonly AttachmentSpool _spool = new();

    private static string MultiRunDir => Path.Combine(RepoLayout.FixturesDir, "multirun");
    private static string ShardedRun => Path.Combine(MultiRunDir, "2026-07-26");
    private static string LaterRun => Path.Combine(MultiRunDir, "2026-07-27");

    public void Dispose() => _spool.Dispose();

    private MultiRunModel Build(params string[] directories)
    {
        var groups = directories
            .Select(d => new RunGroup(
                Path.GetFileName(d),
                Directory.GetFiles(d, "*.ndjson").OrderBy(f => f, StringComparer.Ordinal).ToList()))
            .ToList();

        return MultiRunBuilder.Build(groups, _spool);
    }

    private ScenarioSeries Series(MultiRunModel model, string name) =>
        model.Series.Single(s => s.Name == name);

    [Fact]
    [Criterion("SR-M1")]
    public void ADirectoryOfFilesMergesIntoOneLogicalRun()
    {
        // The sharded directory holds TWO files with two scenarios each.
        var files = Directory.GetFiles(ShardedRun, "*.ndjson");
        Assert.Equal(2, files.Length);

        var model = Build(ShardedRun);

        // One run, not two. Treating each file as a run would produce two bogus trend points
        // and a matrix that is half holes (SPEC §6.1).
        var run = Assert.Single(model.Runs);
        Assert.True(run.WasSharded);
        Assert.Equal(2, run.SourceFiles.Count);

        // All four scenarios, unioned across the shards.
        Assert.Equal(4, run.TotalScenarios);
        Assert.Equal(4, model.Series.Count);

        foreach (var expected in new[] { "Add to basket", "Apply discount", "Checkout", "Refund" })
            Assert.Contains(model.Series, s => s.Name == expected);

        // The merged run spans from the earliest shard to the latest.
        var shards = files.Select(f => RunParser.Parse(f, _spool)).ToList();
        Assert.Equal(shards.Min(s => s.StartedAt), run.StartedAt);

        // And not a single cell is "not run" - every scenario belongs to this run.
        Assert.DoesNotContain(model.Series.SelectMany(s => s.Cells), c => c.Status == CellStatus.NotRun);
    }

    [Fact]
    [Criterion("SR-M2")]
    public void RunsAreOrderedByTheirOwnClockNotByArgumentOrder()
    {
        // Deliberately supplied newest-first. Filename order and argument order are both
        // unreliable - a shell glob yields whatever it yields, and "run-10" sorts before
        // "run-2" (SPEC §6.1).
        var model = Build(LaterRun, ShardedRun);

        Assert.Equal(2, model.Runs.Count);
        Assert.Equal("2026-07-26", model.Runs[0].Label);
        Assert.Equal("2026-07-27", model.Runs[1].Label);

        Assert.True(model.Runs[0].StartedAt < model.Runs[1].StartedAt);

        // The rendered matrix columns follow the same order.
        var html = ReportWriter.RenderMultiRun(model, _spool, new ReportOptions());
        Assert.True(
            html.IndexOf("2026-07-26", StringComparison.Ordinal) <
            html.IndexOf("2026-07-27", StringComparison.Ordinal),
            "Matrix columns must run oldest to newest.");
    }

    [Fact]
    [Criterion("SR-M3")]
    public void NotRunIsDistinctFromSkippedAndExcludedFromFlakiness()
    {
        var model = Build(ShardedRun, LaterRun);

        // "Gift cards" exists only in the later run.
        var giftCards = Series(model, "Gift cards");

        Assert.Equal(CellStatus.NotRun, giftCards.Cells[0].Status);
        Assert.Equal(CellStatus.Passed, giftCards.Cells[1].Status);

        // One executed outcome means no flips, so a scenario that simply did not exist yet is
        // NOT flaky. Counting the gap as a change is the bug this criterion exists to prevent.
        Assert.Single(giftCards.Executed);
        Assert.Equal(0, giftCards.FlipCount);
        Assert.False(giftCards.IsFlaky);

        // NotRun is its own state, never conflated with Skipped.
        Assert.NotEqual(CellStatus.Skipped, giftCards.Cells[0].Status);

        var html = ReportWriter.RenderMultiRun(model, _spool, new ReportOptions());

        // Rendered distinctly, and the report says what a blank cell means rather than leaving
        // the reader to guess.
        Assert.Contains("class=\"cell notrun\"", html, StringComparison.Ordinal);
        Assert.Contains("did not run", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Criterion("SR-M4")]
    public void FlakinessCoversBothCrossRunFlipsAndWithinRunRetries()
    {
        var model = Build(ShardedRun, LaterRun);

        // Cross-run: passed then failed, and failed then passed. Both are flips.
        var discount = Series(model, "Apply discount");
        Assert.Equal(CellStatus.Passed, discount.Cells[0].Status);
        Assert.Equal(CellStatus.Failed, discount.Cells[1].Status);
        Assert.Equal(1, discount.FlipCount);
        Assert.True(discount.IsFlaky);

        var checkout = Series(model, "Checkout");
        Assert.Equal(CellStatus.Failed, checkout.Cells[0].Status);
        Assert.Equal(CellStatus.Passed, checkout.Cells[1].Status);
        Assert.True(checkout.IsFlaky);

        // Within-run: "Refund" failed then passed inside the later run alone. Its cell status
        // is the LAST attempt, so the matrix shows passed - but the flakiness is still caught.
        var refund = Series(model, "Refund");
        Assert.Equal(CellStatus.Passed, refund.Cells[1].Status);
        Assert.Equal(2, refund.Cells[1].AttemptCount);
        Assert.True(refund.Cells[1].FlakyWithinRun);
        Assert.True(refund.IsFlaky);

        // A scenario that passed in every run is not flaky - the list must stay meaningful.
        var stable = Series(model, "Add to basket");
        Assert.False(stable.IsFlaky);

        Assert.Equal(3, model.Flaky.Count());

        var html = ReportWriter.RenderMultiRun(model, _spool, new ReportOptions());
        Assert.Contains("Flaky", html, StringComparison.Ordinal);
        Assert.Contains("Apply discount", html, StringComparison.Ordinal);
        Assert.Contains("within-run", html, StringComparison.Ordinal);

        // The report states what it means by "flaky" - it is a claim about THESE runs, not a
        // statistical property of the scenario (SPEC §6.3).
        Assert.Contains("across the runs", html, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Shape guard, not tied to a criterion: the narrow tests each look at one property, so a
    /// builder that dropped a run or a scenario could still pass them all.
    /// </summary>
    [Fact]
    public void MultiRunModelHasTheExpectedShape()
    {
        var model = Build(ShardedRun, LaterRun);

        Assert.Equal(2, model.Runs.Count);
        Assert.Equal(5, model.Series.Count);           // 4 shared + 1 added later
        Assert.All(model.Series, s => Assert.Equal(2, s.Cells.Count));

        Assert.Equal(4, model.Runs[0].TotalScenarios);
        Assert.Equal(5, model.Runs[1].TotalScenarios);

        // Every identity is unique, or rows would silently merge.
        var identities = model.Series.Select(s => s.Identity).ToList();
        Assert.Equal(identities.Count, identities.Distinct().Count());
    }
}
