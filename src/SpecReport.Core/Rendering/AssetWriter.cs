using SpecReport.Core.Parsing;

namespace SpecReport.Core.Rendering;

/// <summary>
/// Places binary evidence either inline as a data URI or in a sibling folder.
///
/// Inlining is what makes the report render with zero network requests from a file:// URL, but
/// a screenshot-heavy run would produce a file no browser can open. So inlining is bounded: past
/// the budget it degrades to external assets and says so, rather than aborting and leaving the
/// user with no report at all (SPEC §7.1, §8.5).
/// </summary>
internal sealed class AssetWriter(AttachmentSpool spool, ReportOptions options)
{
    private long _inlined;
    private int _next;

    public EvidenceView Attach(EvidenceView view, string spoolId, string mediaType)
    {
        var size = spool.SizeOf(spoolId);
        var useInline = options.Assets == AssetMode.Inline &&
                        _inlined + size <= options.MaxInlineBytes;

        if (options.Assets == AssetMode.Inline && !useInline)
            options.MarkDegraded();

        if (useInline)
        {
            using var stream = spool.OpenRead(spoolId);
            using var memory = new MemoryStream();
            stream.CopyTo(memory);

            _inlined += size;

            return Clone(view, dataUri:
                $"data:{mediaType};base64,{Convert.ToBase64String(memory.ToArray())}");
        }

        Directory.CreateDirectory(options.AssetDirectory);

        var extension = mediaType switch
        {
            "image/png" => ".png",
            "image/jpeg" => ".jpg",
            "image/gif" => ".gif",
            "image/webp" => ".webp",
            _ => ".bin"
        };

        var fileName = $"asset-{++_next:D5}{extension}";
        var path = Path.Combine(options.AssetDirectory, fileName);

        using (var source = spool.OpenRead(spoolId))
        using (var target = File.Create(path))
        {
            source.CopyTo(target);
        }

        var folder = Path.GetFileName(options.AssetDirectory);
        return Clone(view, assetPath: $"{folder}/{fileName}");
    }

    private static EvidenceView Clone(EvidenceView source, string? dataUri = null, string? assetPath = null) =>
        new()
        {
            Kind = source.Kind,
            FileName = source.FileName,
            SizeBytes = source.SizeBytes,
            Text = source.Text,
            Http = source.Http,
            DataUri = dataUri,
            AssetPath = assetPath
        };
}
