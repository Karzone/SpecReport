using System.Text.Json.Serialization;

namespace SpecReport.Core.Rendering;

/// <summary>
/// The shape serialized into the report's data island.
///
/// Deliberately a projection of <see cref="Model.RunModel"/> rather than the model itself: the
/// JS only needs what it renders, and a purpose-built DTO keeps the page's payload small and
/// its script simple. This is also the thing conformance snapshots assert on - it stays stable
/// when styling changes, which HTML goldens do not.
/// </summary>
public sealed class ReportViewModel
{
    public string Title { get; init; } = "SpecReport";
    public string GeneratorVersion { get; init; } = "0.1.0-alpha";

    public string? StartedAt { get; init; }
    public string? FinishedAt { get; init; }
    public double? DurationMs { get; init; }

    public Dictionary<string, int> Counts { get; init; } = [];
    public int TotalScenarios { get; init; }
    public double PassRate { get; init; }

    public List<MetaEntry> Metadata { get; init; } = [];

    /// <summary>Non-null when the input was degraded. Rendered as a banner, never swallowed
    /// (SPEC §7.1).</summary>
    public string? DegradationBanner { get; init; }

    public List<FeatureView> Features { get; init; } = [];

    /// <summary>Every distinct tag in the run, for the filter control.</summary>
    public List<string> AllTags { get; init; } = [];
}

public sealed record MetaEntry(string Label, string Value);

public sealed class FeatureView
{
    public string Uri { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Keyword { get; init; } = "Feature";
    public string Language { get; init; } = "en";
    public List<string> Tags { get; init; } = [];

    /// <summary>Groups within the feature. A rule-less feature has one group with a null name,
    /// so the JS renders one shape rather than two.</summary>
    public List<GroupView> Groups { get; init; } = [];
}

public sealed class GroupView
{
    /// <summary>Rule name, or null for scenarios not inside any Rule.</summary>
    public string? RuleName { get; init; }
    public List<ScenarioView> Scenarios { get; init; } = [];
}

public sealed class ScenarioView
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Keyword { get; init; } = "Scenario";
    public string Status { get; init; } = "unknown";
    public double DurationMs { get; init; }
    public List<string> Tags { get; init; } = [];
    public Dictionary<string, string> ExampleValues { get; init; } = [];

    /// <summary>One-line reason shown on the collapsed row.</summary>
    public string? FailureSummary { get; init; }

    /// <summary>Failed then passed within this run.</summary>
    public bool FlakyWithinRun { get; init; }

    /// <summary>Earlier attempts, if the scenario was retried. The last attempt is
    /// <see cref="Steps"/>.</summary>
    public List<AttemptView> EarlierAttempts { get; init; } = [];

    public List<StepView> Steps { get; init; } = [];

    /// <summary>Evidence captured outside any step, shown at scenario level rather than blamed
    /// on a step (SPEC §3.4).</summary>
    public List<EvidenceView> OutsideStepEvidence { get; init; } = [];
}

public sealed class AttemptView
{
    public int AttemptNumber { get; init; }
    public string Status { get; init; } = "unknown";
    public List<StepView> Steps { get; init; } = [];
}

public sealed class StepView
{
    /// <summary>"gherkin" or "hook".</summary>
    public string Kind { get; init; } = "gherkin";

    public string Keyword { get; init; } = string.Empty;

    /// <summary>Context / Action / Outcome / Conjunction — the styling key, so localized
    /// keywords need no special casing (SPEC §4.3).</summary>
    public string KeywordType { get; init; } = string.Empty;

    public string Text { get; init; } = string.Empty;
    public bool IsBackground { get; init; }
    public string Status { get; init; } = "unknown";

    /// <summary>True when the parser inferred the status rather than reading it. Rendered
    /// distinctly so a guess is never mistaken for reported data (SPEC §4.4).</summary>
    public bool StatusInferred { get; init; }

    public double DurationMs { get; init; }

    public string? ErrorType { get; init; }
    public string? ErrorMessage { get; init; }
    public string? ErrorStack { get; init; }

    public List<List<string>>? DataTable { get; init; }
    public string? DocString { get; init; }

    public List<EvidenceView> Evidence { get; init; } = [];
}

public sealed class EvidenceView
{
    /// <summary>"screenshot" | "http" | "log" | "json" | "file".</summary>
    public string Kind { get; init; } = "file";
    public string? FileName { get; init; }
    public long SizeBytes { get; init; }

    /// <summary>Inline data URI for screenshots. Embedding the bytes is what lets the page
    /// render with ZERO network requests from a file:// URL (SPEC §8.5).</summary>
    public string? DataUri { get; init; }

    /// <summary>Relative path when assets are written externally instead of inlined.</summary>
    public string? AssetPath { get; init; }

    public string? Text { get; init; }

    public HttpView? Http { get; init; }
}

public sealed class HttpView
{
    public int Sequence { get; init; }
    public string Method { get; init; } = "GET";
    public string Url { get; init; } = string.Empty;
    public int? Status { get; init; }
    public string? Reason { get; init; }
    public double DurationMs { get; init; }

    public Dictionary<string, string> RequestHeaders { get; init; } = [];
    public string? RequestBody { get; init; }
    public bool RequestBodyTruncated { get; init; }

    public Dictionary<string, string> ResponseHeaders { get; init; } = [];
    public string? ResponseBody { get; init; }
    public bool ResponseBodyTruncated { get; init; }

    public string? Error { get; init; }

    /// <summary>Which fields were withheld. Always rendered, so the reader knows the request
    /// shown is incomplete rather than assuming it is whole (SPEC §5.2).</summary>
    public List<string> Redactions { get; init; } = [];

    [JsonIgnore]
    public bool HasRedactions => Redactions.Count > 0;
}
