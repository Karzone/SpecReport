using System;
using System.Collections.Generic;

namespace SpecReport.Capture;

/// <summary>
/// One captured request/response pair, serialized as the `http/1` evidence payload from
/// SPEC 3.2. The discriminator lives inside the payload because Reqnroll infers attachment
/// media type from the file extension and offers no way to set a custom one.
/// </summary>
public sealed class HttpExchange
{
    public int Sequence { get; set; }
    public ExchangeScope Scope { get; set; } = ExchangeScope.Step;

    /// <summary>
    /// Zero-based index of the Gherkin step that made this call, or -1 when made outside a step.
    ///
    /// The evidence carries its own step identity rather than relying on the attachment's
    /// `testStepId`. Measured: attachments added from an [AfterStep] hook are stamped with the
    /// AFTER_TEST_STEP *hook's* step id - and every exchange in a scenario ended up sharing one
    /// id - so testStepId cannot tell you which Gherkin step produced the evidence. Carrying the
    /// index makes attribution ours, deterministic, and independent of Reqnroll internals.
    /// </summary>
    public int StepIndex { get; set; } = -1;

    /// <summary>Text of the step that made this call. Human-readable corroboration of
    /// <see cref="StepIndex"/>, and what the report shows if the index cannot be resolved.</summary>
    public string? StepText { get; set; }

    public DateTimeOffset StartedAt { get; set; }
    public double DurationMs { get; set; }

    public string Method { get; set; } = "GET";
    public string Url { get; set; } = string.Empty;
    public IDictionary<string, string> RequestHeaders { get; set; } =
        new Dictionary<string, string>();
    public string? RequestBody { get; set; }
    public bool RequestBodyTruncated { get; set; }
    public long RequestBodyBytes { get; set; }

    public int? Status { get; set; }
    public string? Reason { get; set; }
    public IDictionary<string, string> ResponseHeaders { get; set; } =
        new Dictionary<string, string>();
    public string? ResponseBody { get; set; }
    public bool ResponseBodyTruncated { get; set; }
    public long ResponseBodyBytes { get; set; }

    /// <summary>Set when the request threw instead of producing a response.</summary>
    public string? Error { get; set; }

    /// <summary>Paths that were redacted, so the report shows WHAT was hidden (SPEC 5.2).</summary>
    public IList<string> Redactions { get; set; } = new List<string>();

    public string ToJson()
    {
        var json = new JsonBuilder()
            .Object()
            .Str("$specreport", "http/1")
            .Num("sequence", Sequence)
            .Str("scope", Scope == ExchangeScope.OutsideSteps ? "outside-steps" : "step")
            .Num("stepIndex", StepIndex)
            .Str("stepText", StepText)
            .Str("startedAt", StartedAt.ToString("o"))
            .Num("durationMs", DurationMs);

        json.ObjectMember("request")
            .Str("method", Method)
            .Str("url", Url)
            .Map("headers", RequestHeaders);

        if (RequestBody != null)
        {
            json.Str("body", RequestBody)
                .Bool("bodyTruncated", RequestBodyTruncated)
                .Num("bodyBytes", RequestBodyBytes);
        }

        json.EndObject();

        if (Status.HasValue)
        {
            json.ObjectMember("response")
                .Num("status", Status.Value)
                .Str("reason", Reason)
                .Map("headers", ResponseHeaders);

            if (ResponseBody != null)
            {
                json.Str("body", ResponseBody)
                    .Bool("bodyTruncated", ResponseBodyTruncated)
                    .Num("bodyBytes", ResponseBodyBytes);
            }

            json.EndObject();
        }

        json.Str("error", Error)
            .StringArray("redactions", Redactions)
            .EndObject();

        return json.ToString();
    }
}
