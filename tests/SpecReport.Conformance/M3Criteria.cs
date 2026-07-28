using System.Text.RegularExpressions;
using SpecReport.Cli;
using SpecReport.Core.Model;
using SpecReport.Core.Parsing;
using SpecReport.Core.Rendering;
using Xunit;

namespace SpecReport.Conformance;

/// <summary>
/// Evidence for milestone M3: the single-run HTML report.
///
/// These assert against the actual emitted HTML. That is possible because the report body is
/// rendered server-side rather than built by JavaScript - behaviour like "failed scenarios open
/// by default" is a property of the FILE, so it can be proven without driving a browser.
/// </summary>
public sealed class M3Criteria : IDisposable
{
    private readonly AttachmentSpool _spool = new();
    private readonly List<string> _temporaryPaths = [];

    public void Dispose()
    {
        _spool.Dispose();
        foreach (var path in _temporaryPaths)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { }
        }
    }

    private string RenderFixture(string fixture, ReportOptions? options = null)
    {
        var run = RunParser.Parse(Path.Combine(RepoLayout.FixturesDir, fixture), _spool);
        return ReportWriter.Render(run, _spool, options ?? new ReportOptions());
    }

    private RunModel ParseFixture(string fixture) =>
        RunParser.Parse(Path.Combine(RepoLayout.FixturesDir, fixture), _spool);

    // ---- the contract ------------------------------------------------------------------------

    [Fact]
    [Criterion("SR-C1")]
    public void TheRenderersOnlyInputIsCucumberMessages()
    {
        // Rendering takes a parsed run and a spool of its own attachments - there is no second
        // input, no sidecar file, no config the report depends on. Proven by rendering from
        // nothing but the .ndjson path.
        var html = RenderFixture("sample-run-01.ndjson");

        Assert.Contains("<!DOCTYPE html>", html, StringComparison.Ordinal);
        Assert.Contains("A wholly passing scenario", html, StringComparison.Ordinal);

        // The public surface takes exactly one file path.
        var parse = typeof(RunParser).GetMethod("Parse",
            [typeof(string), typeof(AttachmentSpool)]);
        Assert.NotNull(parse);
    }

    [Fact]
    [Criterion("SR-C2")]
    public void ARunWithoutCaptureStillProducesACompleteTimeline()
    {
        // sample-run-01 was recorded by a project that never referenced SpecReport.Capture.
        var run = ParseFixture("sample-run-01.ndjson");
        var html = RenderFixture("sample-run-01.ndjson");

        Assert.Equal(28, run.AllScenarios.Count());

        // A full timeline: keywords, step text, statuses, failures - all from the built-in
        // formatter's output alone.
        Assert.Contains("Given", html, StringComparison.Ordinal);
        Assert.Contains("the catalogue is seeded", html, StringComparison.Ordinal);
        Assert.Contains("class=\"step passed\"", html, StringComparison.Ordinal);
        Assert.Contains("class=\"step failed\"", html, StringComparison.Ordinal);
        Assert.Contains("Expected basket total", html, StringComparison.Ordinal);

        // No HTTP evidence, because nothing captured any - and that is not an error.
        Assert.DoesNotContain("class=\"http\"",
            RenderFixture("retry-run-04.ndjson"), StringComparison.Ordinal);
    }

    [Fact]
    [Criterion("SR-C3")]
    public void HttpEvidenceIsDetectedByTheInPayloadDiscriminator()
    {
        var run = ParseFixture("capture-run-02.ndjson");

        var http = run.AllScenarios
            .SelectMany(s => (s.Outcome?.Steps ?? []).SelectMany(step => step.Evidence)
                .Concat(s.Outcome?.OutsideStepEvidence ?? []))
            .Where(e => e.Kind == EvidenceKind.HttpExchange)
            .ToList();

        Assert.NotEmpty(http);
        Assert.All(http, e => Assert.NotNull(e.Http));

        // The attachments are plain `application/json` - media type alone cannot distinguish
        // them, so detection must come from the payload's own $specreport field.
        var html = RenderFixture("capture-run-02.ndjson");
        Assert.Contains("class=\"http\"", html, StringComparison.Ordinal);
        Assert.Contains("<span class=\"method\">GET</span>", html, StringComparison.Ordinal);
    }

    [Fact]
    [Criterion("SR-C4")]
    public void UndiscriminatedJsonDegradesToAPlainView()
    {
        var run = ParseFixture("sample-run-01.ndjson");

        var evidence = run.AllScenarios
            .SelectMany(s => s.Outcome?.Steps ?? [])
            .SelectMany(s => s.Evidence)
            .ToList();

        // Evidence.feature attaches a JSON file with no $specreport field.
        var plain = evidence.Where(e => e.Kind == EvidenceKind.Json).ToList();
        Assert.NotEmpty(plain);

        // It must NOT have been mistaken for an HTTP exchange.
        Assert.All(plain, e => Assert.Null(e.Http));
        Assert.Contains(plain, e => (e.InlineText ?? "").Contains("Not SpecReport evidence", StringComparison.Ordinal));

        var html = RenderFixture("sample-run-01.ndjson");
        Assert.Contains("Not SpecReport evidence", html, StringComparison.Ordinal);
    }

    [Fact]
    [Criterion("SR-C5")]
    public void RunMetaSurfacesInTheHeader()
    {
        var run = ParseFixture("hostile-run-05.ndjson");

        Assert.Equal("UAT", run.Metadata.Custom.GetValueOrDefault("environment"));
        Assert.Equal("Chrome 141", run.Metadata.Custom.GetValueOrDefault("browser"));
        Assert.Equal("2 of 4", run.Metadata.Custom.GetValueOrDefault("shard"));

        var html = RenderFixture("hostile-run-05.ndjson");

        // It belongs in the header, not hung off whichever step happened to emit it.
        var header = html[..html.IndexOf("<div class=\"controls\"", StringComparison.Ordinal)];
        Assert.Contains("Environment", header, StringComparison.Ordinal);
        Assert.Contains("UAT", header, StringComparison.Ordinal);
        Assert.Contains("Chrome 141", header, StringComparison.Ordinal);
    }

    // ---- the page ----------------------------------------------------------------------------

    [Fact]
    [Criterion("SR-R1")]
    public void TheReportIssuesZeroNetworkRequests()
    {
        var html = RenderFixture("sample-run-01.ndjson");

        // Any absolute or protocol-relative URL in a fetchable position would break the
        // file:// promise. data: URIs are fine - they are the mechanism that keeps it.
        var offenders = new List<string>();

        void Check(string pattern, string label)
        {
            var matches = Regex.Matches(html, pattern, RegexOptions.IgnoreCase);
            foreach (Match m in matches) offenders.Add($"{label}: {m.Value}");
        }

        Check(@"src\s*=\s*[""'](?!data:)(https?:)?//", "remote src");
        Check(@"href\s*=\s*[""'](?!#)(https?:)?//", "remote href");
        Check(@"@import\s", "css @import");
        Check(@"url\(\s*[""']?(?!data:)(https?:)?//", "css remote url()");
        Check(@"\bfetch\s*\(", "fetch()");
        Check(@"XMLHttpRequest", "XMLHttpRequest");
        Check(@"new\s+WebSocket", "WebSocket");
        Check(@"navigator\.sendBeacon", "sendBeacon");
        Check(@"<link\b", "link element");

        Assert.True(offenders.Count == 0,
            "The report must render identically from a file:// URL on a machine with no " +
            "network, but it references: " + string.Join("; ", offenders.Take(10)));

        // And the screenshots really are embedded, not merely absent.
        Assert.Contains("src=\"data:image/png;base64,", html, StringComparison.Ordinal);
    }

    [Fact]
    [Criterion("SR-R2")]
    public void HostileRunContentCannotExecute()
    {
        var html = RenderFixture("hostile-run-05.ndjson");

        // The fixture's scenario name is `</script><img src=x onerror="alert('xss')">` and its
        // failure message contains a full <script> element.
        //
        // The test is about MARKUP, not substrings: escaped text legitimately contains the
        // characters "onerror=" once the angle brackets and quotes around them are entities.
        // Asserting on the raw substring would fail on correctly-escaped output - which is
        // exactly what it did on the first run.
        Assert.DoesNotContain("<img", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<b>markup", html, StringComparison.OrdinalIgnoreCase);

        // The page has exactly one script element - the embedded behaviour script - and one
        // closing tag to match. Hostile content did not open or close one.
        Assert.Single(Regex.Matches(html, @"<script\b", RegexOptions.IgnoreCase));
        Assert.Single(Regex.Matches(html, @"</script\s*>", RegexOptions.IgnoreCase));

        // No event-handler ATTRIBUTE anywhere in the document.
        //
        // Quoted attribute values are stripped first, because "onerror=" sitting inside
        // data-text="…" is inert data - the surrounding quotes are entity-escaped, so it can
        // never become an attribute. Matching it there would be a false positive, which is what
        // the first version of this check did.
        var withoutAttributeValues = Regex.Replace(html, "\"[^\"]*\"", "\"\"");
        Assert.Empty(Regex.Matches(
            withoutAttributeValues, @"<[a-zA-Z][^>]*\son[a-z]+\s*=", RegexOptions.IgnoreCase));

        // The content is still SHOWN - escaped, not silently dropped, or the report would be
        // lying about what the run contained.
        Assert.Contains("&lt;/script&gt;", html, StringComparison.Ordinal);
        Assert.Contains("&lt;b&gt;markup&lt;/b&gt;", html, StringComparison.Ordinal);
        Assert.Contains("&amp; an ampersand", html, StringComparison.Ordinal);
    }

    [Fact]
    [Criterion("SR-R3")]
    public void FailuresRenderExpandedAndPassesCollapsed()
    {
        var run = ParseFixture("sample-run-01.ndjson");
        var html = RenderFixture("sample-run-01.ndjson");

        var expectedFailures = run.AllScenarios.Count(s => s.Status == StepStatus.Failed);
        Assert.Equal(7, expectedFailures);

        var all = Regex.Matches(html, @"<details class=""scenario"" data-status=""(\w+)""([^>]*)>");
        Assert.Equal(run.AllScenarios.Count(), all.Count);

        var openFailed = 0;
        var openOther = 0;

        foreach (Match match in all)
        {
            var status = match.Groups[1].Value;
            var isOpen = match.Groups[2].Value.Contains(" open", StringComparison.Ordinal);

            if (status == "failed" && isOpen) openFailed++;
            else if (status != "failed" && isOpen) openOther++;
        }

        Assert.Equal(expectedFailures, openFailed);
        Assert.True(openOther == 0,
            $"{openOther} non-failed scenarios render expanded. Passes should be collapsed so " +
            $"the failures are on screen with zero clicks.");
    }

    [Fact]
    [Criterion("SR-R7")]
    public void DegradedInputProducesAVisibleBanner()
    {
        var html = RenderFixture("truncated-run-03.ndjson");

        Assert.Contains("class=\"banner\"", html, StringComparison.Ordinal);
        Assert.Contains("Incomplete input", html, StringComparison.Ordinal);
        Assert.Contains("did not complete", html, StringComparison.OrdinalIgnoreCase);

        // The banner sits in the header, above the scenario list - not buried at the bottom.
        var bannerAt = html.IndexOf("class=\"banner\"", StringComparison.Ordinal);
        var listAt = html.IndexOf("<div id=\"list\">", StringComparison.Ordinal);
        Assert.True(bannerAt < listAt, "The degradation banner must appear before the results.");

        // A clean run must NOT show one - a permanent warning is noise nobody reads.
        Assert.DoesNotContain("class=\"banner\"",
            RenderFixture("sample-run-01.ndjson"), StringComparison.Ordinal);
    }

    [Fact]
    [Criterion("SR-R9")]
    public void RedactionAndTruncationMarkersAreAlwaysRendered()
    {
        var html = RenderFixture("capture-run-02.ndjson");

        // capture-run-02 planted real secrets; redaction hid them and recorded what it hid.
        Assert.Contains("class=\"marker\">redacted:", html, StringComparison.Ordinal);
        Assert.Contains("request.headers.Authorization", html, StringComparison.Ordinal);
        Assert.Contains("request.query.api_key", html, StringComparison.Ordinal);

        // Redacted header values are shown as redacted, not omitted - the reader must know the
        // request displayed is incomplete.
        Assert.Contains("class=\"redacted\"", html, StringComparison.Ordinal);
        Assert.Contains("*** redacted ***", html, StringComparison.Ordinal);

        // And the secrets themselves are nowhere in the rendered page.
        Assert.DoesNotContain("MUST-NOT-LEAK", html, StringComparison.Ordinal);
    }

    // ---- CLI ----------------------------------------------------------------------------------

    [Fact]
    [Criterion("SR-X1")]
    public void ExitCodesDistinguishNothingToDoFromSuccess()
    {
        var output = new StringWriter();
        var error = new StringWriter();

        // No arguments at all: nothing to do.
        Assert.Equal(CommandLine.ExitNothingToDo, CommandLine.Run([], output, error));

        // A path that matches no files: nothing to do.
        Assert.Equal(CommandLine.ExitNothingToDo,
            CommandLine.Run([Path.Combine(Path.GetTempPath(), "specreport-does-not-exist-*.ndjson")],
                            output, error));

        // A real run: success.
        var target = Path.Combine(Path.GetTempPath(), $"specreport-x1-{Guid.NewGuid():N}.html");
        _temporaryPaths.Add(target);

        Assert.Equal(CommandLine.ExitOk, CommandLine.Run(
            [Path.Combine(RepoLayout.FixturesDir, "sample-run-01.ndjson"), "-o", target],
            output, error));

        Assert.True(File.Exists(target));

        // A DEGRADED run still succeeds. `dotnet test` already failed the build for real test
        // failures; failing again here would deny the user the report they most need.
        var degraded = Path.Combine(Path.GetTempPath(), $"specreport-x1d-{Guid.NewGuid():N}.html");
        _temporaryPaths.Add(degraded);

        var degradedOut = new StringWriter();
        Assert.Equal(CommandLine.ExitOk, CommandLine.Run(
            [Path.Combine(RepoLayout.FixturesDir, "truncated-run-03.ndjson"), "-o", degraded],
            degradedOut, error));

        Assert.True(File.Exists(degraded));
        Assert.Contains("warning", degradedOut.ToString(), StringComparison.OrdinalIgnoreCase);
    }
}
