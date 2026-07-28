using System.Text.Json;
using System.Xml.Linq;
using Xunit;

namespace SpecReport.Conformance;

/// <summary>Evidence for the criteria due at milestone M0.</summary>
public sealed class M0Criteria
{
    // ---- SR-C6: Capture stays framework-agnostic -----------------------------------------

    [Fact]
    [Criterion("SR-C6")]
    public void CaptureDeclaresNoDependencyBeyondReqnroll()
    {
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Reqnroll",                 // the one framework dependency
            "NETStandard.Library",      // the BCL itself, implicit for netstandard2.0
            "Microsoft.SourceLink.GitHub" // build-time only, no runtime surface
        };

        var declared = XDocument.Load(RepoLayout.CaptureProject)
            .Descendants("PackageReference")
            .Select(e => e.Attribute("Include")?.Value)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v!)
            .ToList();

        var violations = declared.Where(d => !allowed.Contains(d)).ToList();

        Assert.True(violations.Count == 0,
            $"SpecReport.Capture must stay framework-agnostic (SPEC 3.5) but declares: " +
            $"{string.Join(", ", violations)}. A driver or HTTP library here would be forced on " +
            $"every consumer - use a provider delegate (screenshots) or a DelegatingHandler (HTTP).");
    }

    // ---- SR-C8: Reqnroll can actually auto-load the plugin --------------------------------

    [Fact]
    [Criterion("SR-C8")]
    public void CaptureAssemblyNameEndsWithReqnrollPlugin()
    {
        var assemblyName = XDocument.Load(RepoLayout.CaptureProject)
            .Descendants("AssemblyName")
            .Select(e => e.Value)
            .FirstOrDefault();

        Assert.False(string.IsNullOrWhiteSpace(assemblyName),
            "SpecReport.Capture must set an explicit AssemblyName; Reqnroll discovers runtime " +
            "plugins by file name.");

        Assert.True(assemblyName!.EndsWith(".ReqnrollPlugin", StringComparison.Ordinal),
            $"Reqnroll only auto-loads plugin assemblies whose file name ends in " +
            $"'.ReqnrollPlugin.dll', but AssemblyName is '{assemblyName}'. Renaming this silently " +
            $"stops the plugin loading for every consumer, with no error.");
    }

    // ---- SR-F1: the fixture really covers what M0 promised --------------------------------

    [Fact]
    [Criterion("SR-F1")]
    public void FixtureCoversEveryCaseM0Requires()
    {
        var f = Fixture.SampleRun01;
        var missing = new List<string>();

        void Require(bool condition, string what)
        {
            if (!condition) missing.Add(what);
        }

        Require(f.EnvelopeCount("gherkinDocument") > 0, "gherkinDocument (keywords/Background/Rule)");
        Require(f.EnvelopeCount("pickle") > 0, "pickle");
        Require(f.EnvelopeCount("hook") > 0, "hook envelopes");
        Require(f.EnvelopeCount("attachment") > 0, "attachments");
        Require(f.EnvelopeCount("testRunStarted") == 1, "exactly one testRunStarted");

        Require(f.HasStepStatus("PASSED"), "a PASSED step");
        Require(f.HasStepStatus("FAILED"), "a FAILED step");
        Require(f.HasStepStatus("SKIPPED"), "a SKIPPED step (skipped-after-failure)");
        Require(f.HasStepStatus("PENDING"), "a PENDING step");
        Require(f.HasStepStatus("UNDEFINED"), "an UNDEFINED step");

        Require(f.HasAttachmentMediaType("image/png"), "a PNG screenshot attachment");
        Require(f.HasAttachmentMediaType("application/json"), "a JSON attachment");
        Require(f.HasAttachmentMediaType("text/x.cucumber.log+plain"), "text output attachment");

        Require(f.HasFeatureLanguageOtherThan("en"), "a localized (non-English) feature");
        Require(f.HasRule(), "a Gherkin Rule:");
        Require(f.HasBackground(), "a Background");
        Require(f.HasOutlineWithDuplicateExampleRows(),
            "a Scenario Outline with two byte-identical example rows");
        Require(f.HasDataTable(), "a data table step");
        Require(f.HasDocString(), "a doc string step");
        Require(f.HasHookStep(), "a hook step (hookId rather than pickleStepId)");

        // Every attachment must be step-attributed, or evidence can't hang off a timeline row.
        Require(f.AllAttachmentsHaveStepIds(),
            "all attachments carrying testStepId + testCaseStartedId");

        Assert.True(missing.Count == 0,
            $"Fixture {Fixture.SampleRunFileName} is missing coverage the M0 milestone requires: " +
            string.Join("; ", missing));
    }

    // ---- SR-F2: no accidental failures in the sample --------------------------------------

    /// <summary>
    /// The sample deliberately fails 7 steps. An 8th means someone broke a scenario that was
    /// meant to pass, which would quietly corrupt every downstream expectation built on this
    /// fixture. Asserting the exact number is the cheapest way to notice.
    /// </summary>
    [Fact]
    [Criterion("SR-F2")]
    public void SampleFailsExactlyTheScenariosItIntendsTo()
    {
        const int intendedFailures = 7;
        var actual = Fixture.SampleRun01.CountStepStatus("FAILED");

        Assert.True(actual == intendedFailures,
            $"Expected exactly {intendedFailures} FAILED steps in the sample (1 assertion, " +
            $"1 outline row, 1 rule example, 2 evidence scenarios, 2 hook failures) but found " +
            $"{actual}. If you added an intentional failure, update this count and say so in " +
            $"tests/fixtures/README.md. If you didn't, a scenario that should pass is broken.");
    }
}

/// <summary>Minimal read-only view over a recorded NDJSON fixture.</summary>
internal sealed class Fixture
{
    public const string SampleRunFileName = "sample-run-01.ndjson";

    private static readonly Lazy<Fixture> Lazy = new(() =>
        new Fixture(Path.Combine(RepoLayout.FixturesDir, SampleRunFileName)));

    public static Fixture SampleRun01 => Lazy.Value;

    private readonly List<JsonDocument> _envelopes;

    private Fixture(string path)
    {
        _envelopes = File.ReadLines(path)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Select(line => JsonDocument.Parse(line))
            .ToList();
    }

    private IEnumerable<JsonElement> Of(string envelopeType) =>
        _envelopes.Select(d => d.RootElement)
                  .Where(r => r.TryGetProperty(envelopeType, out _))
                  .Select(r => r.GetProperty(envelopeType));

    public int EnvelopeCount(string envelopeType) => Of(envelopeType).Count();

    private IEnumerable<string> StepStatuses() =>
        Of("testStepFinished")
            .Select(e => e.GetProperty("testStepResult").GetProperty("status").GetString())
            .Where(s => s is not null)
            .Select(s => s!);

    public bool HasStepStatus(string status) =>
        StepStatuses().Any(s => s.Equals(status, StringComparison.OrdinalIgnoreCase));

    public int CountStepStatus(string status) =>
        StepStatuses().Count(s => s.Equals(status, StringComparison.OrdinalIgnoreCase));

    public bool HasAttachmentMediaType(string mediaType) =>
        Of("attachment").Any(a =>
            a.TryGetProperty("mediaType", out var m) &&
            string.Equals(m.GetString(), mediaType, StringComparison.OrdinalIgnoreCase));

    public bool AllAttachmentsHaveStepIds() =>
        Of("attachment").All(a =>
            a.TryGetProperty("testStepId", out var s) && !string.IsNullOrEmpty(s.GetString()) &&
            a.TryGetProperty("testCaseStartedId", out var c) && !string.IsNullOrEmpty(c.GetString()));

    public bool HasFeatureLanguageOtherThan(string language) =>
        Of("gherkinDocument").Any(d =>
            d.TryGetProperty("feature", out var f) &&
            f.TryGetProperty("language", out var l) &&
            !string.Equals(l.GetString(), language, StringComparison.OrdinalIgnoreCase));

    private IEnumerable<JsonElement> FeatureChildren() =>
        Of("gherkinDocument")
            .Where(d => d.TryGetProperty("feature", out _))
            .SelectMany(d => d.GetProperty("feature").GetProperty("children").EnumerateArray());

    public bool HasRule() => FeatureChildren().Any(c => c.TryGetProperty("rule", out _));

    public bool HasBackground() => FeatureChildren().Any(c => c.TryGetProperty("background", out _));

    public bool HasDataTable() =>
        Of("pickle").SelectMany(p => p.GetProperty("steps").EnumerateArray())
            .Any(s => s.TryGetProperty("argument", out var a) &&
                      a.TryGetProperty("dataTable", out _));

    public bool HasDocString() =>
        Of("pickle").SelectMany(p => p.GetProperty("steps").EnumerateArray())
            .Any(s => s.TryGetProperty("argument", out var a) &&
                      a.TryGetProperty("docString", out _));

    public bool HasHookStep() =>
        Of("testCase").SelectMany(tc => tc.GetProperty("testSteps").EnumerateArray())
            .Any(s => s.TryGetProperty("hookId", out var h) && !string.IsNullOrEmpty(h.GetString()));

    /// <summary>
    /// Finds an Examples table containing two rows with identical cell values - the case that
    /// would silently collapse in the multi-run matrix without a per-row discriminator.
    /// </summary>
    public bool HasOutlineWithDuplicateExampleRows()
    {
        foreach (var child in FeatureChildren())
        {
            if (!child.TryGetProperty("scenario", out var scenario)) continue;
            if (!scenario.TryGetProperty("examples", out var examples)) continue;

            foreach (var table in examples.EnumerateArray())
            {
                if (!table.TryGetProperty("tableBody", out var body)) continue;

                var rows = body.EnumerateArray()
                    .Select(r => string.Join('\u0001',
                        r.GetProperty("cells").EnumerateArray().Select(c => c.GetProperty("value").GetString())))
                    .ToList();

                if (rows.Count != rows.Distinct().Count()) return true;
            }
        }

        return false;
    }
}
