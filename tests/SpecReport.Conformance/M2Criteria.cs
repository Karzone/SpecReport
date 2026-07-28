using SpecReport.Core.Model;
using SpecReport.Core.Parsing;
using Xunit;

namespace SpecReport.Conformance;

/// <summary>
/// Evidence for milestone M2: the parser. Asserted against real recorded Reqnroll output
/// (sample-run-01), a truncated copy of it (truncated-run-03), and a hand-authored retry stream
/// (retry-run-04) for the one case Reqnroll will not produce on demand.
/// </summary>
public sealed class M2Criteria : IDisposable
{
    private readonly AttachmentSpool _spool = new();
    private RunModel? _sample;
    private RunModel? _truncated;
    private RunModel? _retry;

    private RunModel Sample => _sample ??= Parse("sample-run-01.ndjson");
    private RunModel Truncated => _truncated ??= Parse("truncated-run-03.ndjson");
    private RunModel Retry => _retry ??= Parse("retry-run-04.ndjson");

    private RunModel Parse(string fixture) =>
        RunParser.Parse(Path.Combine(RepoLayout.FixturesDir, fixture), _spool);

    public void Dispose() => _spool.Dispose();

    /// <summary>
    /// Not tied to a criterion - a shape guard. Every criterion test looks at one narrow
    /// property, so a parser that silently dropped half the run could still pass them all. This
    /// pins the overall counts against the fixture's documented contents.
    /// </summary>
    [Fact]
    public void SampleRunParsesToTheExpectedShape()
    {
        Assert.Equal(6, Sample.Features.Count);

        // 28 scenarios: 6 Basics + 6 Evidence + 2 Hooks + 3 Localised (1 + 2 outline rows)
        // + 7 Outlines rows + 4 Rules. Counted from the feature files and cross-checked against
        // the stream's 28 pickles / 28 testCaseStarted envelopes.
        //
        // NOT MSTest's reported test count, which is 29 - the runner counts one unit the
        // Gherkin model has no scenario for. Taking the runner's number on trust is what this
        // guard caught.
        Assert.Equal(28, Sample.AllScenarios.Count());

        var byStatus = Sample.AllScenarios
            .GroupBy(s => s.Status)
            .ToDictionary(g => g.Key, g => g.Count());

        // 7 failures = 5 assertion failures + 2 hook failures (which have no failed Gherkin step).
        Assert.Equal(7, byStatus.GetValueOrDefault(StepStatus.Failed));
        Assert.Equal(1, byStatus.GetValueOrDefault(StepStatus.Pending));
        Assert.Equal(1, byStatus.GetValueOrDefault(StepStatus.Undefined));
        Assert.Equal(19, byStatus.GetValueOrDefault(StepStatus.Passed));

        // Every scenario resolved to a feature that actually exists in the AST, and none ended
        // up with an empty step list.
        Assert.All(Sample.AllScenarios, s =>
        {
            Assert.NotNull(s.Outcome);
            Assert.NotEmpty(s.Outcome!.Steps);
            Assert.False(string.IsNullOrWhiteSpace(s.Name));
        });

        Assert.All(Sample.Features, f => Assert.False(string.IsNullOrWhiteSpace(f.Name)));
    }

    // ---- keywords and the AST join ---------------------------------------------------------

    [Fact]
    [Criterion("SR-P1")]
    public void StepKeywordsComeFromTheAstJoin()
    {
        var steps = Sample.AllScenarios
            .SelectMany(s => s.Outcome?.Steps ?? [])
            .Where(s => s.Kind == StepKind.Gherkin)
            .ToList();

        Assert.NotEmpty(steps);

        // Pickle steps carry text but NO keyword, so any keyword at all proves the join ran.
        var withoutKeyword = steps.Where(s => string.IsNullOrEmpty(s.Keyword)).ToList();
        Assert.True(withoutKeyword.Count == 0,
            $"{withoutKeyword.Count} steps have no keyword - the gherkinDocument AST join did " +
            $"not resolve them. First: '{withoutKeyword.FirstOrDefault()?.Text}'");

        // And keywords must be trimmed: the AST stores them with a trailing space.
        Assert.DoesNotContain(steps, s => s.Keyword.EndsWith(' '));

        var english = Sample.Features.First(f => f.Language == "en");
        var englishKeywords = english.AllScenarios
            .SelectMany(s => s.Outcome?.Steps ?? [])
            .Where(s => s.Kind == StepKind.Gherkin)
            .Select(s => s.Keyword)
            .Distinct()
            .ToList();

        Assert.Contains("Given", englishKeywords);
        Assert.Contains("When", englishKeywords);
        Assert.Contains("Then", englishKeywords);
    }

    [Fact]
    [Criterion("SR-P2")]
    public void LocalizedKeywordsSurviveAndKeywordTypeIsTheStylingKey()
    {
        var german = Sample.Features.SingleOrDefault(f => f.Language == "de");
        Assert.NotNull(german);

        var steps = german!.AllScenarios
            .SelectMany(s => s.Outcome?.Steps ?? [])
            .Where(s => s.Kind == StepKind.Gherkin)
            .ToList();

        Assert.NotEmpty(steps);

        var keywords = steps.Select(s => s.Keyword).Distinct().ToList();
        Assert.Contains("Angenommen", keywords);
        Assert.Contains("Wenn", keywords);
        Assert.Contains("Dann", keywords);

        // The point of the criterion: styling must key off keywordType, which is
        // language-independent. Code comparing against "Given"/"When"/"Then" breaks here.
        Assert.Contains(steps, s => s.KeywordType == "Context");
        Assert.Contains(steps, s => s.KeywordType == "Action");
        Assert.Contains(steps, s => s.KeywordType == "Outcome");

        Assert.All(steps, s => Assert.False(string.IsNullOrEmpty(s.KeywordType)));

        Assert.Equal("Funktionalität", german.Keyword);
    }

    [Fact]
    [Criterion("SR-P3")]
    public void ScenariosUnderDifferentRulesDoNotMerge()
    {
        var feature = Sample.Features.Single(f => f.Uri.Contains("Rules.feature"));

        Assert.Equal(2, feature.Rules.Count);
        Assert.Contains(feature.Rules, r => r.Name == "In-stock products can be bought");
        Assert.Contains(feature.Rules, r => r.Name == "Out-of-stock products cannot be bought");

        // Every scenario in that feature belongs to a rule; none leaked to the feature level.
        Assert.Empty(feature.Scenarios);
        Assert.All(feature.Rules, r => Assert.NotEmpty(r.Scenarios));

        // Identity carries the rule, so same-named scenarios under different rules stay distinct.
        var identities = feature.AllScenarios.Select(s => s.Identity).ToList();
        Assert.Equal(identities.Count, identities.Distinct().Count());
        Assert.All(identities, i => Assert.NotNull(i.RuleName));
    }

    [Fact]
    [Criterion("SR-P4")]
    public void BackgroundStepsAreIdentifiableAsBackground()
    {
        var scenario = Sample.Features
            .Single(f => f.Uri.Contains("Basics.feature"))
            .Scenarios
            .First(s => s.Name == "A wholly passing scenario");

        var steps = scenario.Outcome!.Steps.Where(s => s.Kind == StepKind.Gherkin).ToList();
        var background = steps.Where(s => s.IsBackground).ToList();

        // Basics.feature has a two-step Background, inlined by the pickle ahead of the
        // scenario's own steps. Without the flag they read as anonymous leading steps.
        Assert.Equal(2, background.Count);
        Assert.Equal("the catalogue is seeded", background[0].Text);
        Assert.Contains(steps, s => !s.IsBackground);
    }

    // ---- hooks ------------------------------------------------------------------------------

    [Fact]
    [Criterion("SR-P5")]
    public void HookStepsAreFirstClassRows()
    {
        var scenario = Sample.AllScenarios.Single(s => s.Name == "Before hook throws");
        var steps = scenario.Outcome!.Steps;

        var hooks = steps.Where(s => s.Kind == StepKind.Hook).ToList();
        Assert.NotEmpty(hooks);

        // The whole reason hook rows exist: this scenario failed with ZERO failed Gherkin steps.
        Assert.DoesNotContain(steps.Where(s => s.Kind == StepKind.Gherkin),
                              s => s.Status == StepStatus.Failed);
        Assert.Contains(hooks, h => h.Status == StepStatus.Failed);
        Assert.Equal(StepStatus.Failed, scenario.Status);

        var failedHook = hooks.Single(h => h.Status == StepStatus.Failed);
        Assert.NotNull(failedHook.Error);
        Assert.Contains("could not seed", failedHook.Error!.Message ?? string.Empty,
                        StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Criterion("SR-P6")]
    public void HookRowsAreLabelledFromTypeAndTagExpression()
    {
        var scenario = Sample.AllScenarios.Single(s => s.Name == "Before hook throws");
        var failedHook = scenario.Outcome!.Steps
            .Single(s => s.Kind == StepKind.Hook && s.Status == StepStatus.Failed);

        // hook.name is always empty, so the label has to be built from what IS carried.
        Assert.Equal(HookKind.BeforeTestCase, failedHook.HookKind);
        Assert.Equal("@fail-before-hook", failedHook.HookTagExpression);
        Assert.Equal("Before hook (@fail-before-hook)", failedHook.HookLabel);
        Assert.Equal(failedHook.HookLabel, failedHook.DisplayText);

        var afterHook = Sample.AllScenarios
            .Single(s => s.Name == "After hook throws though every step passed")
            .Outcome!.Steps
            .Single(s => s.Kind == StepKind.Hook && s.Status == StepStatus.Failed);

        Assert.Equal("After hook (@fail-after-hook)", afterHook.HookLabel);
    }

    // ---- status provenance -------------------------------------------------------------------

    [Fact]
    [Criterion("SR-P7")]
    public void ReportedSkipsWinOverInferenceAndInferredOnesAreMarked()
    {
        var scenario = Sample.AllScenarios
            .Single(s => s.Name == "A failing assertion, with steps skipped after it");

        var steps = scenario.Outcome!.Steps.Where(s => s.Kind == StepKind.Gherkin).ToList();
        var skipped = steps.Where(s => s.Status == StepStatus.Skipped).ToList();

        Assert.NotEmpty(skipped);

        // Reqnroll DOES emit SKIPPED for steps after a failure, so every skip here is reported.
        // Inference is a fallback for silent streams only - it must not be firing.
        Assert.All(skipped, s => Assert.Equal(StatusSource.Reported, s.StatusSource));

        // In a truncated stream the stream goes silent, and inference takes over - marked.
        var inferred = Truncated.AllScenarios
            .SelectMany(s => s.Outcome?.Steps ?? [])
            .Where(s => s.StatusSource == StatusSource.Inferred)
            .ToList();

        Assert.NotEmpty(inferred);
    }

    // ---- retries -----------------------------------------------------------------------------

    [Fact]
    [Criterion("SR-P8")]
    public void RetriedScenariosCountOnceAndTakeTheLastAttempt()
    {
        // Two testCaseStarted envelopes share one testCaseId: one scenario, two attempts.
        var scenario = Assert.Single(Retry.AllScenarios);

        Assert.Equal(2, scenario.Attempts.Count);
        Assert.Equal(0, scenario.Attempts[0].AttemptNumber);
        Assert.Equal(1, scenario.Attempts[1].AttemptNumber);

        Assert.Equal(StepStatus.Failed, scenario.Attempts[0].Status);
        Assert.Equal(StepStatus.Passed, scenario.Attempts[1].Status);

        // The outcome is the LAST attempt - otherwise a retried-and-passing scenario would
        // report as failed and the summary would count it three times.
        Assert.Equal(StepStatus.Passed, scenario.Status);

        // Failed then passed inside one run is a stronger flakiness signal than cross-run
        // flipping, and free once attempts are modelled.
        Assert.True(scenario.IsFlakyWithinRun);
    }

    [Fact]
    [Criterion("SR-P9")]
    public void ByteIdenticalExampleRowsGetDistinctIdentities()
    {
        var outlines = Sample.Features
            .Single(f => f.Uri.Contains("Outlines.feature"))
            .Scenarios
            .Where(s => s.Name == "Discount bands")
            .ToList();

        // "Duplicate rows on purpose" contains two byte-identical rows.
        var duplicates = outlines
            .Where(s => s.ExampleValues.TryGetValue("name", out var n) && n == "Doohick")
            .ToList();

        Assert.Equal(2, duplicates.Count);

        // Same values, different identity - a values-only key would silently merge them and
        // punch a hole in the multi-run matrix.
        Assert.Equal(duplicates[0].ExampleValues, duplicates[1].ExampleValues);
        Assert.NotEqual(duplicates[0].Identity, duplicates[1].Identity);

        var allIdentities = Sample.AllScenarios.Select(s => s.Identity).ToList();
        Assert.Equal(allIdentities.Count, allIdentities.Distinct().Count());
    }

    // ---- degraded input ----------------------------------------------------------------------

    [Fact]
    [Criterion("SR-P10")]
    public void TruncatedStreamsParseBestEffortAndReportWhatWasLost()
    {
        // The killed-CI-run case: no exception, a usable model, and honest diagnostics.
        Assert.NotEmpty(Truncated.Features);
        Assert.NotEmpty(Truncated.AllScenarios);

        var diagnostics = Truncated.Diagnostics;
        Assert.True(diagnostics.RunDidNotComplete,
            "A stream with no testRunFinished must be reported as incomplete.");
        Assert.True(diagnostics.TruncatedFinalLine,
            "The half-written final line must be recognised rather than silently dropped.");
        Assert.True(diagnostics.AnyDegradation);

        var summary = diagnostics.Summary();
        Assert.NotNull(summary);
        Assert.Contains("did not complete", summary!, StringComparison.OrdinalIgnoreCase);

        // A clean stream must NOT claim degradation.
        Assert.False(Sample.Diagnostics.AnyDegradation, Sample.Diagnostics.Summary() ?? "");
    }

    [Fact]
    [Criterion("SR-P11")]
    public void UnknownEnvelopeTypesAndUnknownFieldsAreIgnored()
    {
        // retry-run-04 carries a made-up envelope type and unknown properties on known
        // envelopes. Forward compatibility means the parse still succeeds.
        Assert.NotEmpty(Retry.AllScenarios);

        Assert.Contains("aFutureEnvelopeTypeThatDoesNotExistYet", Retry.Diagnostics.UnknownEnvelopeTypes);

        // Recorded, but not treated as damage - a future Reqnroll release must not make every
        // report look broken.
        Assert.False(Retry.Diagnostics.AnyDegradation,
            "An unknown envelope type is a forward-compatibility event, not a degradation.");

        // The envelope carrying an unknown property still parsed correctly.
        var scenario = Assert.Single(Retry.AllScenarios);
        Assert.Equal(StepStatus.Passed, scenario.Attempts[0].Steps[0].Status);
    }

    [Fact]
    [Criterion("SR-P12")]
    public void DataTablesAndDocStringsSurviveParsing()
    {
        var basics = Sample.Features.Single(f => f.Uri.Contains("Basics.feature"));

        var tableStep = basics.Scenarios
            .Single(s => s.Name == "A data table step")
            .Outcome!.Steps
            .Single(s => s.DataTable is not null);

        var table = tableStep.DataTable!;
        Assert.Equal(["Name", "Price", "Stock"], table.Header);
        Assert.Equal(3, table.Body.Count());
        Assert.Equal(["Widget", "9.99", "12"], table.Body.First());

        var docStep = basics.Scenarios
            .Single(s => s.Name == "A doc string step")
            .Outcome!.Steps
            .Single(s => s.DocString is not null);

        Assert.Contains("gift-wrap", docStep.DocString!, StringComparison.Ordinal);
        Assert.Contains("neighbour", docStep.DocString!, StringComparison.Ordinal);
    }

    [Fact]
    [Criterion("SR-P13")]
    public void TheRunClockComesFromTestRunStarted()
    {
        Assert.NotNull(Sample.StartedAt);
        Assert.NotNull(Sample.FinishedAt);
        Assert.True(Sample.FinishedAt >= Sample.StartedAt);
        Assert.NotNull(Sample.Duration);

        // meta has no timestamp at all, so there is nothing else it could have come from -
        // but assert the value is sane rather than merely present.
        Assert.True(Sample.StartedAt!.Value.Year >= 2026);

        // A truncated run has a start but no finish, and must not invent one.
        Assert.NotNull(Truncated.StartedAt);
        Assert.Null(Truncated.FinishedAt);
        Assert.Null(Truncated.Duration);
    }

    [Fact]
    [Criterion("SR-P14")]
    public void AttachmentBodiesAreSpooledNotHeldInMemory()
    {
        using var spool = new AttachmentSpool();
        var run = RunParser.Parse(
            Path.Combine(RepoLayout.FixturesDir, "sample-run-01.ndjson"), spool);

        var screenshots = run.AllScenarios
            .SelectMany(s => s.Outcome?.Steps ?? [])
            .SelectMany(s => s.Evidence)
            .Where(e => e.Kind == EvidenceKind.Screenshot)
            .ToList();

        Assert.NotEmpty(screenshots);

        // Binary bodies live on disk, referenced by handle - never in the model.
        Assert.All(screenshots, e =>
        {
            Assert.NotNull(e.SpoolId);
            Assert.Null(e.InlineText);
            Assert.True(e.SizeBytes > 0);
        });

        // The spool's high-water mark is the parser's real memory ceiling: O(largest
        // attachment), not O(all of them).
        Assert.True(spool.TotalBytes > spool.LargestBodyBytes,
            "Expected several spooled bodies, so total should exceed the largest.");

        using var stream = spool.OpenRead(screenshots[0].SpoolId!);
        Assert.True(stream.Length > 0);
    }
}
