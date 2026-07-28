using System.Reflection;
using System.Text;
using System.Text.Json;
using Xunit;

namespace SpecReport.Conformance;

/// <summary>
/// Evidence for milestone M1: HTTP exchanges attribute to the step that made them, under
/// parallel execution.
///
/// These assert against <c>capture-run-02.ndjson</c> - real output from a real Reqnroll run of
/// samples/CaptureSampleTests, recorded under MSTest method-level parallelism. Every request
/// URL encodes the step it was made from, so the fixture carries its own ground truth and the
/// assertions are checkable rather than merely plausible.
/// </summary>
public sealed class M1Criteria
{
    [Fact]
    [Criterion("SR-H1")]
    public void EveryStepScopedExchangeLandsOnTheStepThatMadeIt()
    {
        var run = CaptureFixture.Instance;
        var stepScoped = run.Exchanges.Where(e => e.Scope == "step").ToList();

        Assert.True(stepScoped.Count >= 32,
            $"Expected at least 32 step-scoped exchanges (8 parallel scenarios x 4 calls) but " +
            $"found {stepScoped.Count}. Capture may not be running at all.");

        var misattributed = new List<string>();
        var unresolved = new List<string>();

        foreach (var exchange in stepScoped)
        {
            // Ground truth carried in the URL: /probe/{token}
            var token = TokenFromUrl(exchange.Url);
            if (token is null) continue;   // setup calls carry no step token

            // Attribution comes from the payload's OWN step identity, not the attachment's
            // testStepId - measured: attachments added from an [AfterStep] hook are stamped
            // with the AFTER_TEST_STEP hook's id, so testStepId cannot identify the Gherkin
            // step. See docs/reqnroll-api-notes.md 10.
            if (exchange.StepText is null || exchange.StepIndex < 0)
            {
                unresolved.Add($"{token} carries no step identity (index={exchange.StepIndex})");
                continue;
            }

            if (!exchange.StepText.Contains(token, StringComparison.Ordinal))
                misattributed.Add($"call '{token}' recorded against step '{exchange.StepText}'");
        }

        Assert.True(unresolved.Count == 0,
            $"Step-scoped exchanges missing step identity: {string.Join("; ", unresolved)}");

        Assert.True(misattributed.Count == 0,
            $"{misattributed.Count} of {stepScoped.Count} exchanges were attributed to the WRONG " +
            $"step. This is the failure mode the M1 spike exists to prevent - a report that looks " +
            $"plausible but blames the wrong step:{Environment.NewLine}  " +
            string.Join(Environment.NewLine + "  ", misattributed.Take(10)));
    }

    [Fact]
    [Criterion("SR-H2")]
    public void ExchangesMadeOutsideAStepAreBucketedNotMisattributed()
    {
        var run = CaptureFixture.Instance;

        // The sample makes exactly one call from a BeforeScenario hook per scenario.
        var outside = run.Exchanges.Where(e => e.Scope == "outside-steps").ToList();

        Assert.True(outside.Count > 0,
            "No outside-steps exchanges found. The sample makes a call from a BeforeScenario " +
            "hook in every scenario, so they were either dropped or - worse - silently " +
            "attributed to a step.");

        // Every setup call must be bucketed, never step-scoped. A setup call marked "step"
        // means it was blamed on whichever step drained next.
        var leaked = run.Exchanges
            .Where(e => e.Scope == "step" && e.Url.Contains("/setup/", StringComparison.Ordinal))
            .Select(e => e.Url)
            .ToList();

        Assert.True(leaked.Count == 0,
            $"{leaked.Count} setup-time calls were attributed to a Gherkin step instead of the " +
            $"outside-steps bucket: {string.Join(", ", leaked)}");

        Assert.Equal(run.ScenarioCount, outside.Count);
    }

    /// <summary>
    /// Guards the one requirement that fails INVISIBLY. CaptureScope binds an AsyncLocal, and
    /// the M1 spike measured that an assignment made inside an async hook is discarded when the
    /// hook returns (ABSENT 32/32). If someone "modernises" BeginScenario to `async Task`,
    /// ambient capture silently stops working: no exception, no failing test, just an empty
    /// HTTP panel in every report. Nothing else would catch that.
    /// </summary>
    [Fact]
    [Criterion("SR-H3")]
    public void TheCaptureScopeSeedingHookIsSynchronous()
    {
        var method = typeof(global::SpecReport.Capture.CaptureHooks).GetMethod("BeginScenario");
        Assert.NotNull(method);

        Assert.True(method!.ReturnType == typeof(void),
            $"CaptureHooks.BeginScenario must be SYNCHRONOUS (returns void) but returns " +
            $"{method.ReturnType.Name}. Seeding CaptureScope from an async hook silently " +
            $"disables ambient HTTP capture - measured ABSENT 32/32 in the M1 spike. See " +
            $"spikes/M1.HttpAttribution/FINDINGS.md before changing this.");

        Assert.False(
            method.GetCustomAttributes().Any(a => a.GetType().Name == "AsyncStateMachineAttribute"),
            "CaptureHooks.BeginScenario is compiled as an async state machine, which defeats " +
            "the AsyncLocal seeding it exists to perform.");
    }

    private static string? TokenFromUrl(string url)
    {
        const string marker = "/probe/";
        var start = url.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0) return null;

        var rest = url[(start + marker.Length)..];
        var end = rest.IndexOfAny(['?', '&', '/']);
        return end < 0 ? rest : rest[..end];
    }

}

/// <summary>
/// Reads capture-run-02.ndjson and joins the envelopes needed to answer "which step made this
/// call?": attachment -> testStepId -> testCase.testSteps -> pickleStepId -> pickle step text.
/// </summary>
internal sealed class CaptureFixture
{
    private static readonly Lazy<CaptureFixture> Lazy = new(() => new CaptureFixture());
    public static CaptureFixture Instance => Lazy.Value;

    public sealed record Exchange(
        string Scope, string Url, string TestStepId, int Sequence, int StepIndex, string? StepText);

    public IReadOnlyList<Exchange> Exchanges { get; }
    public int ScenarioCount { get; }

    private readonly Dictionary<string, string> _stepTextById = new(StringComparer.Ordinal);

    /// <summary>Resolves a Reqnroll testStepId to its Gherkin step text, where one exists.
    /// Kept for diagnostics: hook steps have no pickleStepId and resolve to null, which is how
    /// we discovered AfterStep attachments are stamped with hook ids.</summary>
    public string? StepTextFor(string testStepId) =>
        _stepTextById.TryGetValue(testStepId, out var text) ? text : null;

    private CaptureFixture()
    {
        var path = Path.Combine(RepoLayout.FixturesDir, "capture-run-02.ndjson");
        var roots = File.ReadLines(path)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Select(l => JsonDocument.Parse(l).RootElement)
            .ToList();

        // pickleStepId -> step text
        var pickleStepText = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pickle in roots.Where(r => r.TryGetProperty("pickle", out _))
                                    .Select(r => r.GetProperty("pickle")))
        {
            foreach (var step in pickle.GetProperty("steps").EnumerateArray())
            {
                var id = step.GetProperty("id").GetString();
                var text = step.GetProperty("text").GetString();
                if (id is not null && text is not null) pickleStepText[id] = text;
            }
        }

        // testStep.id -> step text (via pickleStepId). Hook steps have no pickleStepId and are
        // deliberately absent, so an exchange resolving to one shows up as unresolved.
        foreach (var testCase in roots.Where(r => r.TryGetProperty("testCase", out _))
                                      .Select(r => r.GetProperty("testCase")))
        {
            foreach (var step in testCase.GetProperty("testSteps").EnumerateArray())
            {
                var stepId = step.GetProperty("id").GetString();
                if (stepId is null) continue;

                if (step.TryGetProperty("pickleStepId", out var pickleStepId) &&
                    pickleStepId.GetString() is { } psid &&
                    pickleStepText.TryGetValue(psid, out var text))
                {
                    _stepTextById[stepId] = text;
                }
            }
        }

        ScenarioCount = roots.Count(r => r.TryGetProperty("testCaseStarted", out _));

        var exchanges = new List<Exchange>();
        foreach (var attachment in roots.Where(r => r.TryGetProperty("attachment", out _))
                                        .Select(r => r.GetProperty("attachment")))
        {
            var mediaType = attachment.TryGetProperty("mediaType", out var mt) ? mt.GetString() : null;
            if (mediaType is not "application/json") continue;

            var body = attachment.GetProperty("body").GetString() ?? string.Empty;
            var encoding = attachment.TryGetProperty("contentEncoding", out var ce)
                ? ce.GetString() : "IDENTITY";

            var json = encoding == "BASE64"
                ? Encoding.UTF8.GetString(Convert.FromBase64String(body))
                : body;

            using var payload = JsonDocument.Parse(json);
            var root = payload.RootElement;

            if (!root.TryGetProperty("$specreport", out var disc) ||
                disc.GetString() != "http/1")
            {
                continue;
            }

            exchanges.Add(new Exchange(
                Scope: root.GetProperty("scope").GetString() ?? "?",
                Url: root.GetProperty("request").GetProperty("url").GetString() ?? string.Empty,
                TestStepId: attachment.TryGetProperty("testStepId", out var tsi)
                    ? tsi.GetString() ?? string.Empty
                    : string.Empty,
                Sequence: root.GetProperty("sequence").GetInt32(),
                StepIndex: root.TryGetProperty("stepIndex", out var si) ? si.GetInt32() : -1,
                StepText: root.TryGetProperty("stepText", out var st) ? st.GetString() : null));
        }

        Exchanges = exchanges;
    }
}
