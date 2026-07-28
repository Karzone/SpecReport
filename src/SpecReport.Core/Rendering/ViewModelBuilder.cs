using System.Globalization;
using SpecReport.Core.Model;
using SpecReport.Core.Parsing;

namespace SpecReport.Core.Rendering;

/// <summary>Projects a parsed <see cref="RunModel"/> into the shape the page renders.</summary>
public static class ViewModelBuilder
{
    public static ReportViewModel Build(
        RunModel run, AttachmentSpool spool, ReportOptions options)
    {
        var scenarios = run.AllScenarios.ToList();

        var counts = scenarios
            .GroupBy(s => StatusName(s.Status))
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

        var passed = counts.GetValueOrDefault("passed");

        var assets = new AssetWriter(spool, options);
        var scenarioId = 0;

        var allSteps = scenarios.SelectMany(s => s.Outcome?.Steps ?? []).ToList();
        var allEvidence = allSteps.SelectMany(s => s.Evidence)
            .Concat(scenarios.SelectMany(s => s.Outcome?.OutsideStepEvidence ?? []))
            .ToList();

        var slowest = scenarios
            .Where(s => s.Outcome is not null)
            .OrderByDescending(s => s.Outcome!.Duration)
            .FirstOrDefault();

        return new ReportViewModel
        {
            Title = options.Title ?? DefaultTitle(run),
            StartedAt = run.StartedAt?.ToString("o", CultureInfo.InvariantCulture),
            FinishedAt = run.FinishedAt?.ToString("o", CultureInfo.InvariantCulture),
            DurationMs = run.Duration?.TotalMilliseconds,
            Counts = counts,
            TotalScenarios = scenarios.Count,
            PassRate = scenarios.Count == 0 ? 0 : Math.Round(passed * 100.0 / scenarios.Count, 1),

            TotalSteps = allSteps.Count(s => s.Kind == StepKind.Gherkin),
            FeatureCount = run.Features.Count,
            FlakyCount = scenarios.Count(s => s.IsFlakyWithinRun),
            ScreenshotCount = allEvidence.Count(e => e.Kind == EvidenceKind.Screenshot),
            HttpExchangeCount = allEvidence.Count(e => e.Kind == EvidenceKind.HttpExchange),
            SlowestScenario = slowest?.Name,
            SlowestScenarioMs = slowest?.Outcome?.Duration.TotalMilliseconds ?? 0,
            Metadata = BuildMetadata(run.Metadata),
            DegradationBanner = run.Diagnostics.Summary(),
            AllTags = scenarios.SelectMany(s => s.Tags).Distinct(StringComparer.Ordinal)
                               .OrderBy(t => t, StringComparer.Ordinal).ToList(),
            Features = run.Features.Select(f => BuildFeature(f, assets, ref scenarioId)).ToList()
        };
    }

    private static string DefaultTitle(RunModel run)
    {
        var name = run.Metadata.Custom.GetValueOrDefault("environment");
        return string.IsNullOrWhiteSpace(name) ? "Test run" : $"Test run — {name}";
    }

    private static List<MetaEntry> BuildMetadata(RunMetadata meta)
    {
        var entries = new List<MetaEntry>();

        void Add(string label, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value)) entries.Add(new MetaEntry(label, value!));
        }

        Add("Framework", Join(meta.Implementation, meta.ImplementationVersion));
        Add("Runtime", Join(meta.Runtime, meta.RuntimeVersion));
        Add("OS", meta.OperatingSystem);
        Add("CPU", meta.Cpu);
        Add("CI", meta.CiName);
        Add("Branch", meta.Branch);
        Add("Commit", meta.CommitSha?[..Math.Min(8, meta.CommitSha.Length)]);

        // Custom values come last: they are the run-specific answers (which environment, which
        // browser) a reader looks for first when comparing runs.
        foreach (var (key, value) in meta.Custom.OrderBy(k => k.Key, StringComparer.Ordinal))
            Add(Humanise(key), value);

        return entries;
    }

    private static string? Join(string? a, string? b) =>
        string.IsNullOrWhiteSpace(a) ? null : string.IsNullOrWhiteSpace(b) ? a : $"{a} {b}";

    private static string Humanise(string key) =>
        key.Length == 0 ? key : char.ToUpperInvariant(key[0]) + key[1..];

    private static FeatureView BuildFeature(
        FeatureModel feature, AssetWriter assets, ref int scenarioId)
    {
        var groups = new List<GroupView>();

        if (feature.Scenarios.Count > 0)
        {
            groups.Add(new GroupView
            {
                RuleName = null,
                Scenarios = BuildScenarios(feature.Scenarios, assets, ref scenarioId)
            });
        }

        foreach (var rule in feature.Rules)
        {
            groups.Add(new GroupView
            {
                RuleName = rule.Name,
                Scenarios = BuildScenarios(rule.Scenarios, assets, ref scenarioId)
            });
        }

        return new FeatureView
        {
            Uri = feature.Uri,
            Name = feature.Name,
            Keyword = feature.Keyword,
            Language = feature.Language,
            Tags = feature.Tags.ToList(),
            Groups = groups
        };
    }

    private static List<ScenarioView> BuildScenarios(
        IReadOnlyList<ScenarioModel> scenarios, AssetWriter assets, ref int scenarioId)
    {
        var views = new List<ScenarioView>();

        foreach (var scenario in scenarios)
        {
            var outcome = scenario.Outcome;
            var failure = outcome?.FirstFailure;

            views.Add(new ScenarioView
            {
                Id = "s" + (++scenarioId).ToString(CultureInfo.InvariantCulture),
                Name = scenario.Name,
                Keyword = scenario.Keyword,
                Status = StatusName(scenario.Status),
                DurationMs = outcome?.Duration.TotalMilliseconds ?? 0,
                Tags = scenario.Tags.ToList(),
                ExampleValues = scenario.ExampleValues.ToDictionary(
                    k => k.Key, v => v.Value, StringComparer.Ordinal),
                FailureSummary = failure?.Error?.Message ?? (failure is not null ? "failed" : null),
                FlakyWithinRun = scenario.IsFlakyWithinRun,
                Steps = (outcome?.Steps ?? []).Select(s => BuildStep(s, assets)).ToList(),
                OutsideStepEvidence = (outcome?.OutsideStepEvidence ?? [])
                    .Select(e => BuildEvidence(e, assets)).ToList(),

                // Earlier attempts nest under the scenario rather than duplicating it, so a
                // retried scenario counts once (SPEC §4.5).
                EarlierAttempts = scenario.Attempts.Count <= 1
                    ? []
                    : scenario.Attempts.Take(scenario.Attempts.Count - 1)
                        .Select(a => new AttemptView
                        {
                            AttemptNumber = a.AttemptNumber,
                            Status = StatusName(a.Status),
                            Steps = a.Steps.Select(s => BuildStep(s, assets)).ToList()
                        }).ToList()
            });
        }

        return views;
    }

    private static StepView BuildStep(StepModel step, AssetWriter assets) => new()
    {
        Kind = step.Kind == StepKind.Hook ? "hook" : "gherkin",
        Keyword = step.Kind == StepKind.Hook ? string.Empty : step.Keyword,
        KeywordType = step.KeywordType,
        Text = step.DisplayText,
        IsBackground = step.IsBackground,
        Status = StatusName(step.Status),
        StatusInferred = step.StatusSource == StatusSource.Inferred,
        DurationMs = step.Duration.TotalMilliseconds,
        ErrorType = step.Error?.Type,
        ErrorMessage = step.Error?.Message,
        ErrorStack = step.Error?.StackTrace,
        DataTable = step.DataTable?.Rows.Select(r => r.ToList()).ToList(),
        DocString = step.DocString,
        Evidence = step.Evidence.Select(e => BuildEvidence(e, assets)).ToList()
    };

    private static EvidenceView BuildEvidence(Evidence evidence, AssetWriter assets)
    {
        var view = new EvidenceView
        {
            Kind = evidence.Kind switch
            {
                EvidenceKind.Screenshot => "screenshot",
                EvidenceKind.HttpExchange => "http",
                EvidenceKind.Log => "log",
                EvidenceKind.Json => "json",
                _ => "file"
            },
            FileName = evidence.FileName,
            SizeBytes = evidence.SizeBytes,
            Text = evidence.InlineText,
            Http = evidence.Http is null ? null : new HttpView
            {
                Sequence = evidence.Http.Sequence,
                Method = evidence.Http.Method,
                Url = evidence.Http.Url,
                Status = evidence.Http.Status,
                Reason = evidence.Http.Reason,
                DurationMs = evidence.Http.DurationMs,
                RequestHeaders = evidence.Http.RequestHeaders.ToDictionary(
                    k => k.Key, v => v.Value, StringComparer.Ordinal),
                RequestBody = evidence.Http.RequestBody,
                RequestBodyTruncated = evidence.Http.RequestBodyTruncated,
                ResponseHeaders = evidence.Http.ResponseHeaders.ToDictionary(
                    k => k.Key, v => v.Value, StringComparer.Ordinal),
                ResponseBody = evidence.Http.ResponseBody,
                ResponseBodyTruncated = evidence.Http.ResponseBodyTruncated,
                Error = evidence.Http.Error,
                Redactions = evidence.Http.Redactions.ToList()
            }
        };

        if (evidence.Kind == EvidenceKind.Screenshot && evidence.SpoolId is { } spoolId)
            return assets.Attach(view, spoolId, evidence.MediaType);

        return view;
    }

    internal static string StatusName(StepStatus status) => status switch
    {
        StepStatus.Passed => "passed",
        StepStatus.Failed => "failed",
        StepStatus.Skipped => "skipped",
        StepStatus.Pending => "pending",
        StepStatus.Undefined => "undefined",
        StepStatus.Ambiguous => "ambiguous",
        _ => "unknown"
    };
}
