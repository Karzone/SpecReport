using SpecReport.Core.Model;

namespace SpecReport.Core.Parsing;

/// <summary>
/// Combines the NDJSON files of one logical run.
///
/// CI regularly splits a run across N agents or N test projects, producing N files that are one
/// run. Treating each file as its own run renders bogus trend points and a matrix that is mostly
/// holes - and the flaky list then fires on every scenario "missing" from the shards it never
/// belonged to. Merging is therefore not a nicety; without it multi-run output is confidently
/// wrong on the topology anyone large enough to want trends actually uses (SPEC §6.1).
/// </summary>
public static class RunMerger
{
    /// <summary>
    /// Merges shard files into a single run. Scenarios are unioned; the run starts at the
    /// earliest shard and ends at the latest.
    /// </summary>
    public static RunModel Merge(IReadOnlyList<RunModel> shards)
    {
        if (shards.Count == 0) throw new ArgumentException("No runs to merge.", nameof(shards));
        if (shards.Count == 1) return shards[0];

        var diagnostics = new ParseDiagnostics();

        foreach (var shard in shards)
        {
            diagnostics.UnreadableLines += shard.Diagnostics.UnreadableLines;
            diagnostics.UnfinishedScenarios += shard.Diagnostics.UnfinishedScenarios;
            diagnostics.TruncatedFinalLine |= shard.Diagnostics.TruncatedFinalLine;

            // One incomplete shard makes the whole run incomplete: the results it was carrying
            // are missing from the merged picture.
            diagnostics.RunDidNotComplete |= shard.Diagnostics.RunDidNotComplete;

            foreach (var unknown in shard.Diagnostics.UnknownEnvelopeTypes)
                diagnostics.UnknownEnvelopeTypes.Add(unknown);

            diagnostics.ProtocolVersionWarning ??= shard.Diagnostics.ProtocolVersionWarning;
        }

        var features = shards
            .SelectMany(s => s.Features)
            .GroupBy(f => f.Uri, StringComparer.Ordinal)
            .Select(MergeFeature)
            .OrderBy(f => f.Uri, StringComparer.Ordinal)
            .ToList();

        // Custom metadata is unioned; later shards win on conflict, which is arbitrary but
        // harmless - shards of one run should agree about their environment.
        var custom = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var shard in shards)
        {
            foreach (var (key, value) in shard.Metadata.Custom) custom[key] = value;
        }

        var first = shards[0];

        return new RunModel
        {
            StartedAt = shards.Select(s => s.StartedAt).Where(t => t is not null).Min(),
            FinishedAt = shards.Select(s => s.FinishedAt).Where(t => t is not null).Max(),
            Metadata = new RunMetadata
            {
                ProtocolVersion = first.Metadata.ProtocolVersion,
                Implementation = first.Metadata.Implementation,
                ImplementationVersion = first.Metadata.ImplementationVersion,
                Runtime = first.Metadata.Runtime,
                RuntimeVersion = first.Metadata.RuntimeVersion,
                OperatingSystem = first.Metadata.OperatingSystem,
                Cpu = first.Metadata.Cpu,
                CiName = first.Metadata.CiName,
                CiUrl = first.Metadata.CiUrl,
                CommitSha = first.Metadata.CommitSha,
                Branch = first.Metadata.Branch,
                Custom = custom
            },
            Features = features,
            Diagnostics = diagnostics
        };
    }

    private static FeatureModel MergeFeature(IGrouping<string, FeatureModel> group)
    {
        var parts = group.ToList();
        var head = parts[0];

        var rules = parts
            .SelectMany(f => f.Rules)
            .GroupBy(r => r.Name, StringComparer.Ordinal)
            .Select(g => new RuleModel
            {
                Name = g.Key,
                Keyword = g.First().Keyword,
                Description = g.First().Description,
                Scenarios = Dedupe(g.SelectMany(r => r.Scenarios))
            })
            .ToList();

        return new FeatureModel
        {
            Uri = head.Uri,
            Name = head.Name,
            Keyword = head.Keyword,
            Language = head.Language,
            Description = head.Description,
            Tags = head.Tags,
            Scenarios = Dedupe(parts.SelectMany(f => f.Scenarios)),
            Rules = rules
        };
    }

    /// <summary>
    /// A scenario should appear in exactly one shard. If it appears in several - a re-run, or
    /// overlapping shard definitions - the last one wins, matching the "last attempt is the
    /// outcome" rule used for retries.
    /// </summary>
    private static List<ScenarioModel> Dedupe(IEnumerable<ScenarioModel> scenarios)
    {
        var byIdentity = new Dictionary<ScenarioIdentity, ScenarioModel>();
        var order = new List<ScenarioIdentity>();

        foreach (var scenario in scenarios)
        {
            if (!byIdentity.ContainsKey(scenario.Identity)) order.Add(scenario.Identity);
            byIdentity[scenario.Identity] = scenario;
        }

        return order.Select(id => byIdentity[id]).ToList();
    }
}
