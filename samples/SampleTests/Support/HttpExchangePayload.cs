using System.Text.Json;
using System.Text.Json.Serialization;

namespace SampleTests.Support;

/// <summary>
/// The `http/1` evidence payload from SPEC 3.2. Hand-authored here so M3 can build and test the
/// HTTP panel before SpecReport.Capture exists (M4); Capture will emit this exact shape.
///
/// The discriminator lives INSIDE the payload because Reqnroll's AddAttachment infers media type
/// from the file extension and gives no way to set a custom one — see docs/reqnroll-api-notes.md 3.
/// </summary>
public sealed class HttpExchangePayload
{
    [JsonPropertyName("$specreport")]
    public string Discriminator => "http/1";

    /// <summary>Order within the step. The Cucumber attachment envelope carries no timestamp,
    /// so intra-step ordering has to travel in the payload (SPEC 3.2).</summary>
    public int Sequence { get; init; }

    public DateTimeOffset StartedAt { get; init; }
    public double DurationMs { get; init; }

    public required HttpRequestPart Request { get; init; }
    public HttpResponsePart? Response { get; init; }

    /// <summary>Populated when the exchange threw rather than returning a response.</summary>
    public string? Error { get; init; }

    /// <summary>Paths that were redacted, so the report can show WHAT was hidden, not just that
    /// something was (SPEC 5.2: redaction markers are always visible).</summary>
    public IReadOnlyList<string> Redactions { get; init; } = [];

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public string ToJson() => JsonSerializer.Serialize(this, SerializerOptions);
}

public sealed class HttpRequestPart
{
    public required string Method { get; init; }
    public required string Url { get; init; }
    public IReadOnlyDictionary<string, string> Headers { get; init; } =
        new Dictionary<string, string>();
    public string? Body { get; init; }
    public bool BodyTruncated { get; init; }
    public long BodyBytes { get; init; }
}

public sealed class HttpResponsePart
{
    public int Status { get; init; }
    public string? Reason { get; init; }
    public IReadOnlyDictionary<string, string> Headers { get; init; } =
        new Dictionary<string, string>();
    public string? Body { get; init; }
    public bool BodyTruncated { get; init; }
    public long BodyBytes { get; init; }
}
