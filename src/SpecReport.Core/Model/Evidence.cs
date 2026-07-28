namespace SpecReport.Core.Model;

public enum EvidenceKind
{
    /// <summary>An image - rendered as a thumbnail opening a lightbox.</summary>
    Screenshot,

    /// <summary>A `$specreport: http/1` payload - rendered as the HTTP exchange panel.</summary>
    HttpExchange,

    /// <summary>Text output from IReqnrollOutputHelper.WriteLine.</summary>
    Log,

    /// <summary>JSON with no SpecReport discriminator - pretty-printed, not misread as evidence.</summary>
    Json,

    /// <summary>A `$specreport: runmeta/1` payload. Folded into the run's metadata rather than
    /// rendered on a step - it describes the whole run (SPEC §3.1).</summary>
    RunMeta,

    /// <summary>Anything else - filename, size, download link.</summary>
    File
}

/// <summary>
/// One attachment, classified for rendering. The body is NOT held here: it is spooled to disk
/// during parse and streamed into the output at write time, so peak memory stays O(largest
/// attachment) rather than O(all attachments) - a screenshot-heavy run would otherwise exhaust
/// memory on exactly the runs that motivated streaming (SPEC §8.7).
/// </summary>
public sealed class Evidence
{
    public EvidenceKind Kind { get; init; } = EvidenceKind.File;
    public string MediaType { get; init; } = "application/octet-stream";
    public string? FileName { get; init; }
    public long SizeBytes { get; init; }

    /// <summary>Handle into the spool. Null when the body was inline text short enough to keep.</summary>
    public string? SpoolId { get; init; }

    /// <summary>Kept inline only for small text payloads (log lines), where spooling would cost
    /// more than it saves.</summary>
    public string? InlineText { get; init; }

    /// <summary>Parsed detail for <see cref="EvidenceKind.HttpExchange"/>.</summary>
    public HttpExchangeEvidence? Http { get; init; }

    /// <summary>Key/values from a <see cref="EvidenceKind.RunMeta"/> payload.</summary>
    public IReadOnlyDictionary<string, string>? RunMeta { get; init; }
}

/// <summary>The parts of an `http/1` payload the report renders. Deliberately a projection, not
/// a mirror: the panel shows what a person reading a failure needs.</summary>
public sealed class HttpExchangeEvidence
{
    public int Sequence { get; init; }
    public string Scope { get; init; } = "step";
    public int StepIndex { get; init; } = -1;
    public string? StepText { get; init; }

    public DateTimeOffset? StartedAt { get; init; }
    public double DurationMs { get; init; }

    public string Method { get; init; } = "GET";
    public string Url { get; init; } = string.Empty;
    public IReadOnlyDictionary<string, string> RequestHeaders { get; init; } =
        new Dictionary<string, string>();
    public string? RequestBody { get; init; }
    public bool RequestBodyTruncated { get; init; }

    public int? Status { get; init; }
    public string? Reason { get; init; }
    public IReadOnlyDictionary<string, string> ResponseHeaders { get; init; } =
        new Dictionary<string, string>();
    public string? ResponseBody { get; init; }
    public bool ResponseBodyTruncated { get; init; }

    public string? Error { get; init; }

    /// <summary>Which fields were redacted. Rendered so the reader sees that something was
    /// withheld rather than being shown a quietly incomplete request (SPEC §5.2).</summary>
    public IReadOnlyList<string> Redactions { get; init; } = [];
}
