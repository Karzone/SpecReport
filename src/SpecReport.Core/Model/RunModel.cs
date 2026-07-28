namespace SpecReport.Core.Model;

/// <summary>
/// The parsed shape of one test run. This - not the HTML - is SpecReport's real contract:
/// the renderer serializes it into the report's data island, and conformance snapshots assert
/// against it, because it stays stable when styling changes.
/// </summary>
public sealed class RunModel
{
    /// <summary>From `testRunStarted.timestamp`. The `meta` envelope carries no timestamp
    /// (measured - api-notes §7), so this is the only run clock available.</summary>
    public DateTimeOffset? StartedAt { get; init; }
    public DateTimeOffset? FinishedAt { get; init; }

    public RunMetadata Metadata { get; init; } = new();
    public IReadOnlyList<FeatureModel> Features { get; init; } = [];

    /// <summary>What the parser could not make sense of. Surfaced in the report header rather
    /// than swallowed (SPEC §7.1).</summary>
    public ParseDiagnostics Diagnostics { get; init; } = new();

    public IEnumerable<ScenarioModel> AllScenarios =>
        Features.SelectMany(f => f.AllScenarios);

    public TimeSpan? Duration =>
        StartedAt is { } s && FinishedAt is { } f ? f - s : null;
}

/// <summary>Environment facts from the `meta` envelope, plus anything a `runmeta/1` attachment
/// contributed. All optional: a local run has no CI section (measured).</summary>
public sealed class RunMetadata
{
    public string? ProtocolVersion { get; init; }
    public string? Implementation { get; init; }
    public string? ImplementationVersion { get; init; }
    public string? Runtime { get; init; }
    public string? RuntimeVersion { get; init; }
    public string? OperatingSystem { get; init; }
    public string? Cpu { get; init; }
    public string? CiName { get; init; }
    public string? CiUrl { get; init; }
    public string? CommitSha { get; init; }
    public string? Branch { get; init; }

    /// <summary>Free-form key/values from a `runmeta/1` attachment - environment name, browser,
    /// shard label. The sanctioned escape hatch from SPEC §3.1.</summary>
    public IReadOnlyDictionary<string, string> Custom { get; init; } =
        new Dictionary<string, string>();
}

public sealed class FeatureModel
{
    public string Uri { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;

    /// <summary>The literal Gherkin keyword - "Feature", "Funktionalität", … Displayed as-is
    /// so localized feature files read naturally.</summary>
    public string Keyword { get; init; } = "Feature";

    /// <summary>BCP-47-ish language code from the AST, e.g. "en", "de".</summary>
    public string Language { get; init; } = "en";

    public string? Description { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>Scenarios not inside any Rule.</summary>
    public IReadOnlyList<ScenarioModel> Scenarios { get; init; } = [];

    public IReadOnlyList<RuleModel> Rules { get; init; } = [];

    public IEnumerable<ScenarioModel> AllScenarios =>
        Scenarios.Concat(Rules.SelectMany(r => r.Scenarios));
}

/// <summary>
/// A Gherkin `Rule:` block. It needs its own level because scenarios under different rules must
/// not merge (SPEC §4.2) - the AST nests them one level deep under the feature.
/// </summary>
public sealed class RuleModel
{
    public string Name { get; init; } = string.Empty;
    public string Keyword { get; init; } = "Rule";
    public string? Description { get; init; }
    public IReadOnlyList<ScenarioModel> Scenarios { get; init; } = [];
}
