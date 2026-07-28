using System.Text;
using System.Text.Json;
using SpecReport.Capture;
using SpecReport.Core.Model;
using SpecReport.Core.Parsing;
using Xunit;

namespace SpecReport.Conformance;

/// <summary>
/// Evidence for milestone M4: SpecReport.Capture productized.
///
/// The redaction criteria are proven by ABSENCE. samples/CaptureSampleTests plants five real
/// secrets in captured traffic; these tests assert none of them appears anywhere in the
/// recorded NDJSON. A test that greps for the word "redacted" would pass while leaking.
/// </summary>
public sealed class M4Criteria : IDisposable
{
    private readonly AttachmentSpool _spool = new();

    /// <summary>The exact secrets planted by the sample. Kept as literals rather than
    /// referenced from the sample project, so a rename there cannot quietly disarm the test.</summary>
    private static readonly string[] PlantedSecrets =
    [
        "SPECREPORT-MUST-NOT-LEAK-BEARER-8f3a91",
        "SPECREPORT-MUST-NOT-LEAK-APIKEY-77c2de",
        "SPECREPORT-MUST-NOT-LEAK-NESTEDPW-1a2b3c",
        "SPECREPORT-MUST-NOT-LEAK-NESTEDTOKEN-4d5e6f",
        "SPECREPORT-MUST-NOT-LEAK-NESTEDKEY-7g8h9i"
    ];

    private static string FixturePath => Path.Combine(RepoLayout.FixturesDir, "capture-run-02.ndjson");
    private static string RawFixture => File.ReadAllText(FixturePath);

    public void Dispose() => _spool.Dispose();

    private List<HttpExchangeEvidence> Exchanges()
    {
        var run = RunParser.Parse(FixturePath, _spool);

        return run.AllScenarios
            .SelectMany(s => (s.Outcome?.Steps ?? []).SelectMany(step => step.Evidence)
                .Concat(s.Outcome?.OutsideStepEvidence ?? []))
            .Select(e => e.Http)
            .OfType<HttpExchangeEvidence>()
            .ToList();
    }

    // ---- screenshots --------------------------------------------------------------------------

    [Fact]
    [Criterion("SR-C7")]
    public void WithNoProviderRegisteredNothingIsCapturedAndNothingComplains()
    {
        var raw = RawFixture;

        // The sample has one scenario that registers a provider and one that deliberately does
        // not - both with a failing step, so on-failure capture would fire if it could.
        var run = RunParser.Parse(FixturePath, _spool);

        var withProvider = run.AllScenarios
            .Single(s => s.Name.Contains("provider is registered", StringComparison.Ordinal) &&
                         !s.Name.Contains("No provider", StringComparison.Ordinal));

        var withoutProvider = run.AllScenarios
            .Single(s => s.Name.StartsWith("No provider registered", StringComparison.Ordinal));

        static int Screenshots(ScenarioModel scenario) =>
            (scenario.Outcome?.Steps ?? [])
                .SelectMany(s => s.Evidence)
                .Count(e => e.Kind == EvidenceKind.Screenshot);

        Assert.True(Screenshots(withProvider) > 0,
            "A registered provider plus a failing step should have produced a screenshot.");

        Assert.Equal(0, Screenshots(withoutProvider));

        // And crucially: no error, no warning, no noise. An API suite registering nothing is
        // the NORMAL case, not a misconfiguration to grumble about.
        Assert.DoesNotContain("[SpecReport]", raw, StringComparison.Ordinal);
        Assert.Equal(StepStatus.Failed, withoutProvider.Status);   // it failed on its own terms
    }

    [Fact]
    [Criterion("SR-S6")]
    public async Task OversizedScreenshotsAreDroppedWithAStatedReason()
    {
        // Revised at M4 from "re-encoded to bounded JPEG": re-encoding needs an imaging
        // library, and SPEC §3.5 forbids Capture any dependency beyond Reqnroll. The problem
        // the criterion protects against is unbounded NDJSON growth, so the budget is enforced
        // at the boundary instead. See criteria.json revisedNote.
        var options = new ScreenshotOptions { MaxBytes = 1024 };

        var oversized = await ScreenshotResult.TryCaptureAsync(
            _ => Task.FromResult<byte[]?>(new byte[4096]), options, CancellationToken.None);

        Assert.Null(oversized.Bytes);
        Assert.NotNull(oversized.Skipped);
        Assert.Contains("exceeds", oversized.Skipped!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("4,096", oversized.Skipped!, StringComparison.Ordinal);

        // Within budget, it is captured.
        var accepted = await ScreenshotResult.TryCaptureAsync(
            _ => Task.FromResult<byte[]?>(new byte[512]), options, CancellationToken.None);

        Assert.NotNull(accepted.Bytes);
        Assert.Null(accepted.Skipped);

        // A provider that throws - usually a browser that already crashed, which is often WHY
        // the step failed - must never propagate into the consumer's run.
        var threw = await ScreenshotResult.TryCaptureAsync(
            _ => throw new InvalidOperationException("driver is gone"),
            options, CancellationToken.None);

        Assert.Null(threw.Bytes);
        Assert.Contains("driver is gone", threw.Skipped!, StringComparison.Ordinal);
    }

    [Fact]
    [Criterion("SR-C9")]
    public void ScreenshotsRenderAgainstTheStepThatProducedThem()
    {
        // fullstack-run-06 has two deliberately failing scenarios, each with a browser open, so
        // on-failure capture fires on the LAST step of each.
        var path = Path.Combine(RepoLayout.FixturesDir, "fullstack-run-06.ndjson");
        var run = RunParser.Parse(path, _spool);

        var withShots = run.AllScenarios
            .Select(s => new
            {
                s.Name,
                Steps = (s.Outcome?.Steps ?? []).Where(st => st.Kind == StepKind.Gherkin).ToList()
            })
            .Select(x => new
            {
                x.Name,
                Shot = x.Steps.FirstOrDefault(st => st.Evidence.Any(e => e.Kind == EvidenceKind.Screenshot)),
                Failed = x.Steps.FirstOrDefault(st => st.Status == StepStatus.Failed)
            })
            .Where(x => x.Shot is not null)
            .ToList();

        Assert.NotEmpty(withShots);

        foreach (var entry in withShots)
        {
            Assert.NotNull(entry.Failed);

            // The screenshot belongs on the step that failed. Before the fix it landed on step
            // 1 every time, because Reqnroll stamps an [AfterStep] attachment with the
            // AFTER_TEST_STEP hook's id rather than the Gherkin step's (api-notes §10).
            Assert.True(ReferenceEquals(entry.Shot, entry.Failed),
                $"In '{entry.Name}' the screenshot rendered against '{entry.Shot!.Text}' but the " +
                $"step that failed was '{entry.Failed!.Text}'.");
        }
    }

    // ---- redaction -----------------------------------------------------------------------------

    [Fact]
    [Criterion("SR-S1")]
    public void SensitiveHeadersAreRedactedAtCaptureTime()
    {
        var exchanges = Exchanges();

        var authorization = exchanges
            .Where(e => e.RequestHeaders.ContainsKey("Authorization"))
            .ToList();

        Assert.NotEmpty(authorization);
        Assert.All(authorization, e =>
            Assert.Equal("*** redacted ***", e.RequestHeaders["Authorization"]));

        // Response headers too - Set-Cookie is a session token by another name.
        var cookies = exchanges.Where(e => e.ResponseHeaders.ContainsKey("Set-Cookie")).ToList();
        Assert.NotEmpty(cookies);
        Assert.All(cookies, e => Assert.Equal("*** redacted ***", e.ResponseHeaders["Set-Cookie"]));

        // Non-sensitive headers pass through, or the report would be useless.
        Assert.Contains(exchanges, e =>
            e.ResponseHeaders.TryGetValue("X-Request-ID", out var id) && id == "req-0001");
    }

    [Fact]
    [Criterion("SR-S2")]
    public void SensitiveQueryParametersAreRedacted()
    {
        var exchanges = Exchanges();

        var withApiKey = exchanges.Where(e => e.Url.Contains("api_key=", StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(withApiKey);

        Assert.All(withApiKey, e =>
        {
            Assert.DoesNotContain("77c2de", e.Url, StringComparison.Ordinal);
            Assert.Contains("request.query.api_key", e.Redactions);

            // The rest of the URL survives - the reader still needs to know which endpoint
            // was called, and which page.
            Assert.Contains("/probe/redact-a", e.Url, StringComparison.Ordinal);
            Assert.Contains("page=2", e.Url, StringComparison.Ordinal);
        });
    }

    [Fact]
    [Criterion("SR-S3")]
    public void SensitiveJsonBodyKeysAreRedactedAtAnyDepth()
    {
        var nested = Exchanges().Single(e =>
            e.Url.Contains("/probe/redact-b", StringComparison.Ordinal));

        var body = nested.RequestBody;
        Assert.NotNull(body);

        // Four levels down, under an innocuous parent - and inside an array element.
        Assert.Contains("request.body.order.customer.credentials.password", nested.Redactions);
        Assert.Contains("request.body.order.customer.credentials.refresh_token", nested.Redactions);
        Assert.Contains("request.body.order.items.apiKey", nested.Redactions);

        Assert.DoesNotContain("NESTEDPW", body!, StringComparison.Ordinal);
        Assert.DoesNotContain("NESTEDTOKEN", body!, StringComparison.Ordinal);
        Assert.DoesNotContain("NESTEDKEY", body!, StringComparison.Ordinal);

        // Non-sensitive siblings survive - over-redacting the whole body would make the panel
        // worthless.
        Assert.Contains("\"name\": \"Ada\"", body!, StringComparison.Ordinal);
        Assert.Contains("\"username\": \"ada\"", body!, StringComparison.Ordinal);
        Assert.Contains("\"sku\": \"ABC\"", body!, StringComparison.Ordinal);

        // The result is still valid JSON, or the report cannot pretty-print it.
        using var parsed = JsonDocument.Parse(body!);
        Assert.Equal(JsonValueKind.Object, parsed.RootElement.ValueKind);
    }

    [Fact]
    [Criterion("SR-S4")]
    public void NoPlantedSecretReachesTheNdjson()
    {
        // The whole-file check, because a secret can hide in a header, a URL, a body, a log
        // line, or a base64 blob. This is the assertion that actually matters.
        var raw = RawFixture;

        var leaked = PlantedSecrets.Where(s => raw.Contains(s, StringComparison.Ordinal)).ToList();

        Assert.True(leaked.Count == 0,
            $"{leaked.Count} planted secret(s) reached the NDJSON, which CI archives as an " +
            $"artifact: {string.Join(", ", leaked)}");

        // Base64-encoded attachment bodies are inside that file too, so decode and re-check
        // rather than trusting the raw scan alone.
        var decoded = new StringBuilder();
        foreach (var line in File.ReadLines(FixturePath))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            using var document = JsonDocument.Parse(line);

            if (!document.RootElement.TryGetProperty("attachment", out var attachment)) continue;
            if (!attachment.TryGetProperty("body", out var body)) continue;

            var text = body.GetString() ?? string.Empty;
            var isBase64 = attachment.TryGetProperty("contentEncoding", out var enc) &&
                           enc.GetString() == "BASE64";

            if (!isBase64) { decoded.Append(text); continue; }

            try { decoded.Append(Encoding.UTF8.GetString(Convert.FromBase64String(text))); }
            catch (FormatException) { /* binary attachment - no text to search */ }
        }

        var inAttachments = PlantedSecrets
            .Where(s => decoded.ToString().Contains(s, StringComparison.Ordinal))
            .ToList();

        Assert.True(inAttachments.Count == 0,
            $"Secret(s) found inside decoded attachment bodies: {string.Join(", ", inAttachments)}");
    }

    [Fact]
    [Criterion("SR-S5")]
    public void OversizedBodiesAreTruncatedWithAVisibleMarker()
    {
        var oversized = Exchanges().Single(e =>
            e.Url.Contains("/probe/limit-a", StringComparison.Ordinal));

        Assert.True(oversized.ResponseBodyTruncated,
            "A 200 KB response against a 64 KB cap must be marked truncated.");

        Assert.NotNull(oversized.ResponseBody);
        Assert.True(oversized.ResponseBody!.Length <= 64 * 1024,
            $"Captured {oversized.ResponseBody.Length} bytes despite the 64 KB cap.");

        // The marker has to reach the page, not just the model - a silently short body reads
        // as the whole response (SPEC §5.2).
        var run = RunParser.Parse(FixturePath, _spool);
        var html = Core.Rendering.ReportWriter.Render(
            run, _spool, new Core.Rendering.ReportOptions());

        Assert.Contains("body truncated", html, StringComparison.Ordinal);
    }

    [Fact]
    [Criterion("SR-S7")]
    public void TheReadmeStatesWhatRedactionDoesNotCover()
    {
        var readme = File.ReadAllText(RepoLayout.ReadmePath);

        Assert.Contains("what it covers, and what it doesn't", readme, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("NOT redacted", readme, StringComparison.OrdinalIgnoreCase);

        // Each real gap named explicitly. An overclaimed security guarantee is worse than
        // none, so these are the ones a reader must not have to discover the hard way.
        Assert.Contains("Non-JSON bodies", readme, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("screenshot", readme, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path segment", readme, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("innocuous keys", readme, StringComparison.OrdinalIgnoreCase);

        // And it must point at the escape hatches, or the warning is just bad news.
        Assert.Contains("BodyScrubber", readme, StringComparison.Ordinal);
        Assert.Contains("SensitiveNameFragments", readme, StringComparison.Ordinal);
    }
}
