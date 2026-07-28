using System.Text.Json;
using System.Text.Json.Serialization;

namespace SpecReport.Conformance;

public sealed record Criterion(
    string Id,
    string Spec,
    string Kind,
    string Milestone,
    string Statement,
    string? ManualNote = null)
{
    public bool IsExecutable => Kind.Equals("executable", StringComparison.OrdinalIgnoreCase);
    public bool IsManual => Kind.Equals("manual", StringComparison.OrdinalIgnoreCase);
}

public sealed record Signoff(
    string Id,
    string Milestone,
    string SignedOffBy,
    string Date,
    string? Notes = null);

public sealed class CriteriaDocument
{
    public string CurrentMilestone { get; init; } = "M0";
    public IReadOnlyList<string> Milestones { get; init; } = [];
    public IReadOnlyList<Criterion> Criteria { get; init; } = [];
}

public sealed class SignoffDocument
{
    public IReadOnlyList<Signoff> Signoffs { get; init; } = [];
}

public static class Registry
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    private static readonly Lazy<CriteriaDocument> LazyCriteria = new(() =>
        JsonSerializer.Deserialize<CriteriaDocument>(
            File.ReadAllText(RepoLayout.CriteriaPath), Options)
        ?? throw new InvalidOperationException("criteria.json deserialized to null."));

    private static readonly Lazy<SignoffDocument> LazySignoffs = new(() =>
        File.Exists(RepoLayout.SignoffPath)
            ? JsonSerializer.Deserialize<SignoffDocument>(
                  File.ReadAllText(RepoLayout.SignoffPath), Options) ?? new SignoffDocument()
            : new SignoffDocument());

    public static CriteriaDocument Criteria => LazyCriteria.Value;
    public static IReadOnlyList<Signoff> Signoffs => LazySignoffs.Value.Signoffs;

    /// <summary>Ordinal of a milestone in the declared sequence; -1 if unknown.</summary>
    public static int Ordinal(string milestone)
    {
        for (var i = 0; i < Criteria.Milestones.Count; i++)
        {
            if (string.Equals(Criteria.Milestones[i], milestone, StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return -1;
    }

    /// <summary>
    /// Criteria the gate enforces right now: everything due at or before the current milestone.
    /// Advancing <c>currentMilestone</c> in criteria.json is what tightens the gate.
    /// </summary>
    public static IEnumerable<Criterion> InScope()
    {
        var now = Ordinal(Criteria.CurrentMilestone);
        return Criteria.Criteria.Where(c => Ordinal(c.Milestone) <= now && Ordinal(c.Milestone) >= 0);
    }

    public static IEnumerable<Criterion> OutOfScope()
    {
        var now = Ordinal(Criteria.CurrentMilestone);
        return Criteria.Criteria.Where(c => Ordinal(c.Milestone) > now);
    }

    public static bool IsSignedOff(Criterion criterion) =>
        Signoffs.Any(s => string.Equals(s.Id, criterion.Id, StringComparison.OrdinalIgnoreCase));
}
