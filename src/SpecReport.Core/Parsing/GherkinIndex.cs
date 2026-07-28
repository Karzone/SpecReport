using System.Text.Json;
using SpecReport.Core.Model;

namespace SpecReport.Core.Parsing;

/// <summary>
/// Indexes `gherkinDocument` envelopes so pickles can be resolved back to the feature file.
///
/// THE AST JOIN IS MANDATORY, NOT AN OPTIMISATION. A pickle step carries `text` and `type` but
/// **no keyword** (measured), and Background steps are inlined into the pickle with nothing to
/// mark them. Without this index the report cannot render "Given"/"When"/"Then" at all, shows
/// five mystery steps at the top of every scenario, and merges scenarios that live under
/// different `Rule:` blocks (SPEC §4.2, §4.3).
/// </summary>
internal sealed class GherkinIndex
{
    private readonly Dictionary<string, AstStep> _steps = new(StringComparer.Ordinal);
    private readonly Dictionary<string, AstScenario> _scenarios = new(StringComparer.Ordinal);
    private readonly Dictionary<string, AstExampleRow> _exampleRows = new(StringComparer.Ordinal);
    private readonly Dictionary<string, AstFeature> _features = new(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, AstFeature> Features => _features;

    internal sealed record AstStep(
        string Id, string Keyword, string KeywordType, string Text, bool IsBackground);

    internal sealed record AstScenario(
        string Id, string Keyword, string Name, string? Description,
        IReadOnlyList<string> Tags, string FeatureUri, string? RuleName, int Order);

    internal sealed record AstExampleRow(
        string Id, IReadOnlyDictionary<string, string> Values, int RowIndex);

    internal sealed record AstFeature(
        string Uri, string Keyword, string Name, string? Description,
        string Language, IReadOnlyList<string> Tags, IReadOnlyList<string> RuleOrder);

    public AstStep? Step(string id) => _steps.TryGetValue(id, out var s) ? s : null;
    public AstScenario? Scenario(string id) => _scenarios.TryGetValue(id, out var s) ? s : null;
    public AstExampleRow? ExampleRow(string id) => _exampleRows.TryGetValue(id, out var r) ? r : null;
    public AstFeature? Feature(string uri) => _features.TryGetValue(uri, out var f) ? f : null;

    public void Add(JsonElement document)
    {
        var uri = document.Str("uri") ?? string.Empty;
        var feature = document.Prop("feature");
        if (feature is null) return;

        var f = feature.Value;
        var ruleOrder = new List<string>();
        var order = 0;

        foreach (var child in f.Array("children"))
        {
            if (child.Prop("background") is { } background)
            {
                IndexSteps(background, isBackground: true);
            }
            else if (child.Prop("scenario") is { } scenario)
            {
                IndexScenario(scenario, uri, ruleName: null, ref order);
            }
            else if (child.Prop("rule") is { } rule)
            {
                // Rules nest one level: their own Background plus their own scenarios.
                var ruleName = rule.Str("name") ?? string.Empty;
                ruleOrder.Add(ruleName);

                foreach (var ruleChild in rule.Array("children"))
                {
                    if (ruleChild.Prop("background") is { } ruleBackground)
                        IndexSteps(ruleBackground, isBackground: true);
                    else if (ruleChild.Prop("scenario") is { } ruleScenario)
                        IndexScenario(ruleScenario, uri, ruleName, ref order);
                }
            }
        }

        _features[uri] = new AstFeature(
            Uri: uri,
            Keyword: f.Str("keyword") ?? "Feature",
            Name: f.Str("name") ?? string.Empty,
            Description: Trim(f.Str("description")),
            Language: f.Str("language") ?? "en",
            Tags: TagNames(f),
            RuleOrder: ruleOrder);
    }

    private void IndexScenario(JsonElement scenario, string uri, string? ruleName, ref int order)
    {
        var id = scenario.Str("id");
        if (id is null) return;

        _scenarios[id] = new AstScenario(
            Id: id,
            Keyword: scenario.Str("keyword") ?? "Scenario",
            Name: scenario.Str("name") ?? string.Empty,
            Description: Trim(scenario.Str("description")),
            Tags: TagNames(scenario),
            FeatureUri: uri,
            RuleName: ruleName,
            Order: order++);

        IndexSteps(scenario, isBackground: false);
        IndexExamples(scenario);
    }

    private void IndexSteps(JsonElement container, bool isBackground)
    {
        foreach (var step in container.Array("steps"))
        {
            var id = step.Str("id");
            if (id is null) continue;

            _steps[id] = new AstStep(
                Id: id,
                // AST keywords carry a trailing space - "Given ", "Angenommen " (measured).
                Keyword: (step.Str("keyword") ?? string.Empty).Trim(),
                KeywordType: step.Str("keywordType") ?? string.Empty,
                Text: step.Str("text") ?? string.Empty,
                IsBackground: isBackground);
        }
    }

    /// <summary>
    /// Indexes Examples rows by id. Each row gets its own id even when two rows are
    /// byte-identical (measured), which is what stops the multi-run matrix silently merging
    /// duplicate rows (SPEC §6.2).
    /// </summary>
    private void IndexExamples(JsonElement scenario)
    {
        foreach (var examples in scenario.Array("examples"))
        {
            var header = examples.Prop("tableHeader")?.Array("cells")
                                 .Select(c => c.Str("value") ?? string.Empty)
                                 .ToList() ?? [];

            var rowIndex = 0;
            foreach (var row in examples.Array("tableBody"))
            {
                var id = row.Str("id");
                if (id is null) { rowIndex++; continue; }

                var values = new Dictionary<string, string>(StringComparer.Ordinal);
                var cells = row.Array("cells").Select(c => c.Str("value") ?? string.Empty).ToList();

                for (var i = 0; i < cells.Count && i < header.Count; i++)
                    values[header[i]] = cells[i];

                _exampleRows[id] = new AstExampleRow(id, values, rowIndex++);
            }
        }
    }

    private static IReadOnlyList<string> TagNames(JsonElement element) =>
        element.Array("tags")
               .Select(t => t.Str("name"))
               .Where(n => n is not null)
               .Select(n => n!.TrimStart('@'))
               .ToList();

    private static string? Trim(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
