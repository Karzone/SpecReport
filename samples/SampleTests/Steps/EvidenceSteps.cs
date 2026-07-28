using System.Text;
using System.Text.Json;
using SampleTests.Support;
using Reqnroll;

namespace SampleTests.Steps;

/// <summary>
/// Produces the attachment envelopes the report's evidence renderers dispatch on (SPEC 3.2).
/// Attachments go through IReqnrollOutputHelper.AddAttachment, which Reqnroll stamps with the
/// CURRENT step id automatically — see docs/reqnroll-api-notes.md 3.
/// </summary>
[Binding]
public sealed class EvidenceSteps(IReqnrollOutputHelper output, ScenarioContext scenario)
{
    private int _httpSequence;

    [Then("I attach a screenshot")]
    [When("I attach a screenshot")]
    public void AttachScreenshot() => output.AddAttachment(ScratchFiles.ScreenshotPath());

    [When("I record a successful HTTP exchange")]
    public void RecordSuccessfulExchange() => AttachExchange(BuildExchange(status: 200));

    [When("I record a failing HTTP exchange")]
    public void RecordFailingExchange() => AttachExchange(BuildExchange(status: 503));

    [When("I record (.*) HTTP exchanges")]
    public void RecordSeveralExchanges(int count)
    {
        for (var i = 0; i < count; i++)
            AttachExchange(BuildExchange(status: i == 1 ? 404 : 200));
    }

    [When("I write some text output")]
    public void WriteTextOutput()
    {
        output.WriteLine($"Scenario '{scenario.ScenarioInfo.Title}' reached the output step.");
        output.WriteLine("A second line, so the renderer's multi-line handling is exercised.");
    }

    [When("I attach a plain JSON file")]
    public void AttachPlainJson()
    {
        // No "$specreport" discriminator: the report must degrade this to a plain JSON view
        // rather than mistaking it for an HTTP exchange (SPEC 3.2).
        var json = JsonSerializer.Serialize(
            new { note = "Not SpecReport evidence.", items = new[] { 1, 2, 3 } },
            new JsonSerializerOptions { WriteIndented = true });

        output.AddAttachment(ScratchFiles.WriteText("plain-payload.json", json));
    }

    private void AttachExchange(HttpExchangePayload payload)
        => output.AddAttachment(
            ScratchFiles.WriteText($"http-{payload.Sequence:D2}.json", payload.ToJson()));

    private HttpExchangePayload BuildExchange(int status)
    {
        _httpSequence++;

        var failed = status >= 400;

        return new HttpExchangePayload
        {
            Sequence = _httpSequence,
            // Fixed instant: the fixture must be byte-stable across runs so snapshot tests
            // don't churn. Real capture uses the actual clock.
            StartedAt = new DateTimeOffset(2026, 7, 28, 9, 15, 0, TimeSpan.Zero)
                .AddMilliseconds(_httpSequence * 250),
            DurationMs = 40 + (_httpSequence * 22),
            Request = new HttpRequestPart
            {
                Method = failed ? "POST" : "GET",
                Url = $"https://api.example.com/v2/orders?apiKey=%E2%97%8F%E2%97%8F%E2%97%8F&page={_httpSequence}",
                Headers = new Dictionary<string, string>
                {
                    ["Accept"] = "application/json",
                    ["Content-Type"] = "application/json",
                    ["Authorization"] = "*** redacted ***",
                    ["X-Correlation-Id"] = $"corr-{_httpSequence:D4}"
                },
                Body = failed
                    ? JsonSerializer.Serialize(new { sku = "ABC-123", qty = 2 })
                    : null,
                BodyBytes = failed ? 30 : 0
            },
            Response = new HttpResponsePart
            {
                Status = status,
                Reason = status switch
                {
                    200 => "OK",
                    404 => "Not Found",
                    503 => "Service Unavailable",
                    _ => null
                },
                Headers = new Dictionary<string, string>
                {
                    ["Content-Type"] = "application/json; charset=utf-8",
                    ["x-request-id"] = $"7f3a-{_httpSequence:D4}"
                },
                Body = failed
                    ? """{ "error": "upstream_unavailable", "retryAfter": 30 }"""
                    : """{ "orders": [ { "id": 1, "total": 9.99 } ], "page": 1 }""",
                BodyBytes = 64
            },
            Redactions = ["request.headers.Authorization", "request.query.apiKey"]
        };
    }
}

/// <summary>
/// Attachments must be real files on disk — AddAttachment takes a path, reads the bytes, and
/// inlines them base64 into the NDJSON.
/// </summary>
internal static class ScratchFiles
{
    private static readonly string Root =
        Path.Combine(Path.GetTempPath(), "specreport-sample");

    public static string ScreenshotPath()
    {
        Directory.CreateDirectory(Root);
        var path = Path.Combine(Root, "screenshot.png");
        if (!File.Exists(path))
            File.WriteAllBytes(path, SamplePng.Bytes());
        return path;
    }

    public static string WriteText(string fileName, string content)
    {
        Directory.CreateDirectory(Root);
        // Unique per call so several attachments in one scenario don't overwrite each other.
        var path = Path.Combine(Root, $"{Guid.NewGuid():N}-{fileName}");
        File.WriteAllText(path, content, Encoding.UTF8);
        return path;
    }
}
