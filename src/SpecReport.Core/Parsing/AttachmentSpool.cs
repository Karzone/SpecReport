namespace SpecReport.Core.Parsing;

/// <summary>
/// Holds attachment bodies on disk during parsing instead of in the run model.
///
/// Reqnroll inlines every attachment into the NDJSON as base64 with no size cap (measured), so
/// a screenshot-heavy run means hundreds of megabytes of bodies. Keeping them in the model
/// would make peak memory O(total attachment bytes) - and the runs big enough to matter are
/// exactly the ones that would then fail to open. Spooling keeps it O(largest attachment)
/// (SPEC §8.7).
///
/// The spool is a temp directory deleted on <see cref="Dispose"/>; the renderer streams from it
/// while writing the report.
/// </summary>
public sealed class AttachmentSpool : IDisposable
{
    private readonly string _root;
    private int _next;
    private bool _disposed;

    public AttachmentSpool(string? root = null)
    {
        _root = root ?? Path.Combine(Path.GetTempPath(), "specreport-spool-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    /// <summary>Largest single body seen, which is the parser's real memory high-water mark.</summary>
    public long LargestBodyBytes { get; private set; }

    public long TotalBytes { get; private set; }
    public int Count { get; private set; }

    /// <summary>Writes a body to the spool and returns the handle to reference it by.</summary>
    public string Write(byte[] body)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var id = (++_next).ToString("D6");
        File.WriteAllBytes(Path.Combine(_root, id), body);

        Count++;
        TotalBytes += body.Length;
        if (body.Length > LargestBodyBytes) LargestBodyBytes = body.Length;

        return id;
    }

    /// <summary>Opens a spooled body for streaming. The caller disposes the stream.</summary>
    public Stream OpenRead(string spoolId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return File.OpenRead(Path.Combine(_root, spoolId));
    }

    public long SizeOf(string spoolId) => new FileInfo(Path.Combine(_root, spoolId)).Length;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp directory is not worth failing a report over.
        }
    }
}
