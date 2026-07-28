using System.Text;
using System.Text.Json;
using SpecReport.Core.Model;

namespace SpecReport.Core.Parsing;

/// <summary>
/// Classifies an attachment for rendering.
///
/// Dispatch is on media type PLUS an in-payload discriminator, because Reqnroll infers media
/// type from the file extension and gives no way to set a custom one - a vendor media type like
/// `application/vnd.specreport.http+json` is unreachable (measured). A JSON attachment without
/// the discriminator must render as plain JSON rather than be misread as evidence (SPEC §3.2).
/// </summary>
internal static class EvidenceFactory
{
    /// <summary>Text bodies under this size stay in memory; anything larger is spooled. Log
    /// lines are tiny and spooling them would cost more than it saves.</summary>
    private const int InlineTextLimit = 8 * 1024;

    public static Evidence Create(
        string mediaType, string body, bool isBase64, string? fileName, AttachmentSpool spool)
    {
        var bytes = isBase64 ? SafeDecode(body) : Encoding.UTF8.GetBytes(body);

        if (mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            return new Evidence
            {
                Kind = EvidenceKind.Screenshot,
                MediaType = mediaType,
                FileName = fileName,
                SizeBytes = bytes.Length,
                SpoolId = spool.Write(bytes)
            };
        }

        if (mediaType.Contains("json", StringComparison.OrdinalIgnoreCase))
            return CreateJson(mediaType, bytes, fileName, spool);

        if (mediaType.StartsWith("text/", StringComparison.OrdinalIgnoreCase))
        {
            var text = Encoding.UTF8.GetString(bytes);
            return new Evidence
            {
                Kind = EvidenceKind.Log,
                MediaType = mediaType,
                FileName = fileName,
                SizeBytes = bytes.Length,
                InlineText = bytes.Length <= InlineTextLimit ? text : null,
                SpoolId = bytes.Length > InlineTextLimit ? spool.Write(bytes) : null
            };
        }

        return new Evidence
        {
            Kind = EvidenceKind.File,
            MediaType = mediaType,
            FileName = fileName,
            SizeBytes = bytes.Length,
            SpoolId = spool.Write(bytes)
        };
    }

    private static Evidence CreateJson(
        string mediaType, byte[] bytes, string? fileName, AttachmentSpool spool)
    {
        var text = Encoding.UTF8.GetString(bytes);

        try
        {
            using var document = JsonDocument.Parse(text);
            var root = document.RootElement;

            switch (root.Str("$specreport"))
            {
                case "http/1":
                    return new Evidence
                    {
                        Kind = EvidenceKind.HttpExchange,
                        MediaType = mediaType,
                        FileName = fileName,
                        SizeBytes = bytes.Length,
                        Http = ReadHttp(root)
                    };

                case "runmeta/1":
                    return new Evidence
                    {
                        Kind = EvidenceKind.RunMeta,
                        MediaType = mediaType,
                        FileName = fileName,
                        SizeBytes = bytes.Length,
                        RunMeta = root.StringMap("values")
                    };
            }
        }
        catch (JsonException)
        {
            // Malformed JSON in an attachment is the producer's problem, not a reason to fail
            // the report. It degrades to a plain file.
        }

        return new Evidence
        {
            Kind = EvidenceKind.Json,
            MediaType = mediaType,
            FileName = fileName,
            SizeBytes = bytes.Length,
            InlineText = bytes.Length <= InlineTextLimit ? text : null,
            SpoolId = bytes.Length > InlineTextLimit ? spool.Write(bytes) : null
        };
    }

    private static HttpExchangeEvidence ReadHttp(JsonElement root)
    {
        var request = root.Prop("request");
        var response = root.Prop("response");

        return new HttpExchangeEvidence
        {
            Sequence = root.Int("sequence") ?? 0,
            Scope = root.Str("scope") ?? "step",
            StepIndex = root.Int("stepIndex") ?? -1,
            StepText = root.Str("stepText"),
            StartedAt = DateTimeOffset.TryParse(root.Str("startedAt"), out var started)
                ? started
                : null,
            DurationMs = root.Prop("durationMs")?.GetDouble() ?? 0,

            Method = request?.Str("method") ?? "GET",
            Url = request?.Str("url") ?? string.Empty,
            RequestHeaders = request?.StringMap("headers") ?? new Dictionary<string, string>(),
            RequestBody = request?.Str("body"),
            RequestBodyTruncated = request?.Bool("bodyTruncated") ?? false,

            Status = response?.Int("status"),
            Reason = response?.Str("reason"),
            ResponseHeaders = response?.StringMap("headers") ?? new Dictionary<string, string>(),
            ResponseBody = response?.Str("body"),
            ResponseBodyTruncated = response?.Bool("bodyTruncated") ?? false,

            Error = root.Str("error"),
            Redactions = root.StringArray("redactions")
        };
    }

    private static byte[] SafeDecode(string body)
    {
        try
        {
            return Convert.FromBase64String(body);
        }
        catch (FormatException)
        {
            // Corrupt base64 in a truncated stream. Preserve what is there as text rather than
            // failing the whole report.
            return Encoding.UTF8.GetBytes(body);
        }
    }
}
