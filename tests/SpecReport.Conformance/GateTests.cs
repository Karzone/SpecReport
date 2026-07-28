using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Xunit;

namespace SpecReport.Conformance;

/// <summary>
/// The gate. These tests police the criteria registry itself rather than the product.
///
/// The load-bearing one is <see cref="EveryInScopeExecutableCriterionHasEvidence"/>: it makes
/// "nobody wrote a test for this" a BUILD FAILURE. Without it a registry entry with no test is
/// indistinguishable from a passing one, and the conformance board becomes decoration.
/// </summary>
public sealed class GateTests
{
    [Fact]
    public void CriterionIdsAreUnique()
    {
        var duplicates = Registry.Criteria.Criteria
            .GroupBy(c => c.Id, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        Assert.True(duplicates.Count == 0,
            $"Duplicate criterion ids in criteria.json: {string.Join(", ", duplicates)}");
    }

    [Fact]
    public void EveryCriterionDeclaresAKnownMilestone()
    {
        var bad = Registry.Criteria.Criteria
            .Where(c => Registry.Ordinal(c.Milestone) < 0)
            .Select(c => $"{c.Id} -> '{c.Milestone}'")
            .ToList();

        Assert.True(bad.Count == 0,
            $"Criteria referencing milestones not in the declared sequence: {string.Join(", ", bad)}");
    }

    [Fact]
    public void EveryCriterionDeclaresAKnownKind()
    {
        var bad = Registry.Criteria.Criteria
            .Where(c => !c.IsExecutable && !c.IsManual)
            .Select(c => $"{c.Id} -> '{c.Kind}'")
            .ToList();

        Assert.True(bad.Count == 0,
            $"Criteria with a kind other than executable/manual: {string.Join(", ", bad)}");
    }

    /// <summary>
    /// Guards drift between the registry and the spec. It can only prove the referenced SECTION
    /// exists, not that the statement still matches the prose - that stays a review
    /// responsibility, and pretending otherwise would be exactly the false assurance this whole
    /// mechanism exists to avoid.
    /// </summary>
    [Fact]
    public void EveryCriterionReferencesARealSpecSection()
    {
        var spec = File.ReadAllText(RepoLayout.SpecPath);
        var headings = new HashSet<string>(
            Regex.Matches(spec, @"(?m)^#{2,4}\s+(\d+(?:\.\d+)*)\.?\s")
                 .Select(m => m.Groups[1].Value));

        var missing = Registry.Criteria.Criteria
            .Where(c => !headings.Contains(c.Spec))
            .Select(c => $"{c.Id} -> SPEC section '{c.Spec}'")
            .ToList();

        Assert.True(missing.Count == 0,
            $"Criteria pointing at spec sections that don't exist: {string.Join("; ", missing)}. " +
            $"Known sections: {string.Join(", ", headings.OrderBy(h => h))}");
    }

    [Fact]
    public void EveryTaggedTestReferencesAKnownCriterion()
    {
        var known = Registry.Criteria.Criteria
            .Select(c => c.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var unknown = Evidence.All()
            .Where(e => !known.Contains(e.CriterionId))
            .Select(e => $"{e.TestName} -> '{e.CriterionId}'")
            .ToList();

        Assert.True(unknown.Count == 0,
            $"Tests tagged with criterion ids that aren't in criteria.json: {string.Join("; ", unknown)}");
    }

    /// <summary>THE GATE. Missing evidence is a failure, never a silent pass.</summary>
    [Fact]
    public void EveryInScopeExecutableCriterionHasEvidence()
    {
        var covered = Evidence.All()
            .Select(e => e.CriterionId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var unproven = Registry.InScope()
            .Where(c => c.IsExecutable && !covered.Contains(c.Id))
            .ToList();

        var detail = string.Join(
            Environment.NewLine,
            unproven.Select(c => $"  {c.Id} (SPEC {c.Spec}, due {c.Milestone}): {c.Statement}"));

        Assert.True(unproven.Count == 0,
            $"{unproven.Count} in-scope executable criteria have NO test proving them. " +
            $"Current milestone is {Registry.Criteria.CurrentMilestone}. Either write the test, or " +
            $"push the criterion's milestone out - do not leave it unproven:" +
            Environment.NewLine + detail);
    }

    /// <summary>
    /// Manual criteria can't be auto-verified, so they require a recorded human sign-off in
    /// docs/manual-signoff.json. Reaching their milestone without one fails the gate: the
    /// alternative is quietly shipping a claim nobody checked.
    /// </summary>
    [Fact]
    public void EveryInScopeManualCriterionIsSignedOff()
    {
        var pending = Registry.InScope()
            .Where(c => c.IsManual && !Registry.IsSignedOff(c))
            .ToList();

        var detail = string.Join(
            Environment.NewLine,
            pending.Select(c => $"  {c.Id} (SPEC {c.Spec}): {c.Statement}"));

        Assert.True(pending.Count == 0,
            $"{pending.Count} manual criteria are due at milestone " +
            $"{Registry.Criteria.CurrentMilestone} but have no entry in docs/manual-signoff.json. " +
            $"A human has to look at these and record the result:" +
            Environment.NewLine + detail);
    }

    [Fact]
    public void SignoffsReferenceKnownCriteria()
    {
        var known = Registry.Criteria.Criteria
            .Select(c => c.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var orphans = Registry.Signoffs
            .Where(s => !known.Contains(s.Id))
            .Select(s => s.Id)
            .ToList();

        Assert.True(orphans.Count == 0,
            $"Sign-offs for criteria that no longer exist: {string.Join(", ", orphans)}");
    }

    /// <summary>
    /// Not an assertion - emits docs/conformance.md so the current state is readable without
    /// running anything. Named so it's obvious in the runner that it proves nothing.
    /// </summary>
    [Fact]
    public void EmitConformanceReport()
    {
        var covered = Evidence.All()
            .GroupBy(e => e.CriterionId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Select(e => e.TestName).ToList(),
                          StringComparer.OrdinalIgnoreCase);

        var sb = new StringBuilder();
        sb.AppendLine("# Conformance");
        sb.AppendLine();
        sb.AppendLine("<!-- GENERATED by SpecReport.Conformance. Do not edit by hand. -->");
        sb.AppendLine();
        sb.AppendLine($"Current milestone: **{Registry.Criteria.CurrentMilestone}** " +
                      $"of {string.Join(" -> ", Registry.Criteria.Milestones)}");
        sb.AppendLine();
        sb.AppendLine("Criteria at or below the current milestone are enforced: an executable one");
        sb.AppendLine("with no test, or a manual one with no recorded sign-off, fails the build.");
        sb.AppendLine();

        var inScope = Registry.InScope().OrderBy(c => c.Id).ToList();
        sb.AppendLine($"## In scope ({inScope.Count})");
        sb.AppendLine();
        sb.AppendLine("| Criterion | SPEC | Kind | Status | Evidence |");
        sb.AppendLine("|---|---|---|---|---|");

        foreach (var c in inScope)
        {
            var (status, evidence) = c.IsManual
                ? Registry.IsSignedOff(c)
                    ? ("signed off", SignoffText(c))
                    : ("**AWAITING SIGN-OFF**", "-")
                : covered.TryGetValue(c.Id, out var tests)
                    ? ("proven", string.Join("<br>", tests.Select(t => $"`{t}`")))
                    : ("**UNPROVEN**", "-");

            sb.AppendLine($"| **{c.Id}** — {c.Statement} | {c.Spec} | {c.Kind} | {status} | {evidence} |");
        }

        var later = Registry.OutOfScope()
            .OrderBy(c => Registry.Ordinal(c.Milestone)).ThenBy(c => c.Id).ToList();

        sb.AppendLine();
        sb.AppendLine($"## Scheduled for later milestones ({later.Count})");
        sb.AppendLine();
        sb.AppendLine("| Criterion | SPEC | Kind | Due |");
        sb.AppendLine("|---|---|---|---|");
        foreach (var c in later)
            sb.AppendLine($"| **{c.Id}** — {c.Statement} | {c.Spec} | {c.Kind} | {c.Milestone} |");

        File.WriteAllText(RepoLayout.ReportPath, sb.ToString());
    }

    private static string SignoffText(Criterion c)
    {
        var s = Registry.Signoffs.First(x =>
            string.Equals(x.Id, c.Id, StringComparison.OrdinalIgnoreCase));
        return $"{s.SignedOffBy}, {s.Date}";
    }
}

/// <summary>Discovers which criteria the test suite actually carries evidence for.</summary>
internal static class Evidence
{
    internal sealed record Item(string CriterionId, string TestName);

    public static IReadOnlyList<Item> All()
    {
        var items = new List<Item>();

        foreach (var type in Assembly.GetExecutingAssembly().GetTypes())
        {
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance |
                                                   BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                var isTest = method.GetCustomAttributes()
                    .Any(a => a.GetType().Name is "FactAttribute" or "TheoryAttribute");
                if (!isTest) continue;

                foreach (var tag in method.GetCustomAttributes<CriterionAttribute>())
                    items.Add(new Item(tag.Id, $"{type.Name}.{method.Name}"));

                foreach (var tag in type.GetCustomAttributes<CriterionAttribute>())
                    items.Add(new Item(tag.Id, $"{type.Name}.{method.Name}"));
            }
        }

        return items;
    }
}
