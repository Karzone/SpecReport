namespace SpecReport.Core.Rendering;

public enum AssetMode
{
    /// <summary>Embed binaries as data URIs. Produces one self-contained file that renders
    /// from a file:// URL with zero network requests.</summary>
    Inline,

    /// <summary>Write binaries beside the report in a sibling folder. For runs whose
    /// screenshots would make a single file unusable.</summary>
    External
}

public sealed class ReportOptions
{
    public string? Title { get; set; }
    public AssetMode Assets { get; set; } = AssetMode.Inline;

    /// <summary>Where the report is being written; external assets go in a sibling folder.</summary>
    public string OutputPath { get; set; } = "specreport.html";

    /// <summary>
    /// Above this total, inlining auto-degrades to external assets rather than producing a file
    /// nobody can open. Degrading beats aborting: you lose asset embedding, not the report
    /// (SPEC §7.1).
    /// </summary>
    public long MaxInlineBytes { get; set; } = 50L * 1024 * 1024;

    /// <summary>Set when inlining degraded, so the page can say so.</summary>
    public bool DegradedToExternal { get; private set; }

    internal void MarkDegraded() => DegradedToExternal = true;

    public string AssetDirectory =>
        Path.Combine(
            Path.GetDirectoryName(OutputPath) is { Length: > 0 } dir ? dir : ".",
            Path.GetFileNameWithoutExtension(OutputPath) + "_files");
}
