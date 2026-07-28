using System.Reflection;
using System.Text;
using System.Text.Json;
using SpecReport.Core.Model;
using SpecReport.Core.Parsing;

namespace SpecReport.Core.Rendering;

/// <summary>
/// Writes a self-contained HTML report.
///
/// The body is rendered server-side by <see cref="HtmlBuilder"/> and the embedded script only
/// adds interactivity, so the report is readable with JavaScript disabled - it is an archived
/// CI artifact, and a blank page in a locked-down browser is a bad failure mode. The build
/// stays pure .NET with no Node toolchain (SPEC §11).
/// </summary>
public static class ReportWriter
{
    public static void Write(RunModel run, AttachmentSpool spool, ReportOptions options)
    {
        var html = Render(run, spool, options);

        var directory = Path.GetDirectoryName(Path.GetFullPath(options.OutputPath));
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        File.WriteAllText(options.OutputPath, html, new UTF8Encoding(false));
    }

    /// <summary>Renders to a string. Used by tests, which have no reason to touch the disk.</summary>
    public static string Render(RunModel run, AttachmentSpool spool, ReportOptions options)
    {
        var view = ViewModelBuilder.Build(run, spool, options);
        return Render(view);
    }

    public static string Render(ReportViewModel view) =>
        Compose(view.Title, HtmlBuilder.BuildBody(view));

    /// <summary>Renders a multi-run report: trend, flaky list, matrix, and per-run drill-down.</summary>
    public static string RenderMultiRun(
        MultiRunModel model, AttachmentSpool spool, ReportOptions options)
    {
        var title = options.Title ?? $"Test runs — {model.Runs.Count} runs";
        return Compose(title, MultiRunHtmlBuilder.BuildBody(model, title, spool, options));
    }

    public static void WriteMultiRun(
        MultiRunModel model, AttachmentSpool spool, ReportOptions options)
    {
        var html = RenderMultiRun(model, spool, options);

        var directory = Path.GetDirectoryName(Path.GetFullPath(options.OutputPath));
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        File.WriteAllText(options.OutputPath, html, new UTF8Encoding(false));
    }

    private static string Compose(string title, string body) =>
        Resource("report.html")
            .Replace("{{TITLE}}", HtmlEscape(title), StringComparison.Ordinal)
            .Replace("{{STYLE}}", Resource("report.css"), StringComparison.Ordinal)
            .Replace("{{BODY}}", body, StringComparison.Ordinal)
            .Replace("{{SCRIPT}}", Resource("report.js"), StringComparison.Ordinal);

    /// <summary>
    /// The view model as JSON. Not embedded in the page - it exists so conformance tests can
    /// snapshot the parsed shape, which stays stable when styling changes (SPEC §11).
    /// </summary>
    public static string SerializeViewModel(ReportViewModel view) =>
        JsonSerializer.Serialize(view, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = true
        });

    private static string Resource(string name)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var fullName = $"SpecReport.Core.Rendering.Resources.{name}";

        using var stream = assembly.GetManifestResourceStream(fullName)
            ?? throw new InvalidOperationException(
                $"Embedded resource '{fullName}' is missing. Available: " +
                string.Join(", ", assembly.GetManifestResourceNames()));

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static string HtmlEscape(string value) => value
        .Replace("&", "&amp;", StringComparison.Ordinal)
        .Replace("<", "&lt;", StringComparison.Ordinal)
        .Replace(">", "&gt;", StringComparison.Ordinal)
        .Replace("\"", "&quot;", StringComparison.Ordinal);
}
