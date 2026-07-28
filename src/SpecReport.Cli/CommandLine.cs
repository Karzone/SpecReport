using System.Diagnostics;
using SpecReport.Core.Parsing;
using SpecReport.Core.Rendering;

namespace SpecReport.Cli;

/// <summary>
/// `specreport` - turns a Reqnroll run's Cucumber Messages into an HTML report.
///
/// Exit codes are deliberately blunt (SPEC §7):
///   0  a report was produced, warnings and all
///   2  nothing to do - no input files, or no readable envelopes in them
///
/// A degraded run still exits 0. `dotnet test` has already failed the build for real test
/// failures; failing again here would only make CI logs confusing while denying the user the
/// report they most need - the one from the run that died.
/// </summary>
public static class CommandLine
{
    public const int ExitOk = 0;
    public const int ExitNothingToDo = 2;

    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        if (args.Length == 0)
        {
            WriteUsage(error);
            return ExitNothingToDo;
        }

        if (args[0] is "-h" or "--help" or "help")
        {
            WriteUsage(output);
            return ExitOk;
        }

        Arguments parsed;
        try
        {
            parsed = Arguments.Parse(args);
        }
        catch (ArgumentException ex)
        {
            error.WriteLine($"specreport: {ex.Message}");
            WriteUsage(error);
            return ExitNothingToDo;
        }

        var groups = ResolveGroups(parsed.Paths);

        if (groups.Count == 0)
        {
            error.WriteLine(
                "specreport: no .ndjson files found. Enable Reqnroll's `message` formatter in " +
                "reqnroll.json, then point this at the file it writes.");
            return ExitNothingToDo;
        }

        var options = new ReportOptions
        {
            OutputPath = parsed.Output,
            Title = parsed.Title,
            Assets = parsed.External ? AssetMode.External : AssetMode.Inline
        };

        using var spool = new AttachmentSpool();

        if (groups.Count > 1)
            return RunMultiple(groups, options, spool, parsed, output);

        var group = groups[0];
        var shards = group.Files.Select(f => RunParser.Parse(f, spool)).ToList();
        var run = RunMerger.Merge(shards);
        var scenarioCount = run.AllScenarios.Count();

        if (scenarioCount == 0 && run.Diagnostics.UnreadableLines > 0)
        {
            error.WriteLine(
                $"specreport: '{group.Files[0]}' contained no readable envelopes " +
                $"({run.Diagnostics.UnreadableLines} unreadable line(s)).");
            return ExitNothingToDo;
        }

        ReportWriter.Write(run, spool, options);

        var merged = group.Files.Count > 1 ? $" (merged from {group.Files.Count} files)" : "";
        output.WriteLine(
            $"specreport: {scenarioCount} scenario(s){merged} -> {Path.GetFullPath(options.OutputPath)}");

        if (run.Diagnostics.Summary() is { } warning)
            output.WriteLine($"specreport: warning - {warning}");

        if (options.DegradedToExternal)
        {
            output.WriteLine(
                $"specreport: warning - assets exceeded the inline budget and were written to " +
                $"{options.AssetDirectory}");
        }

        if (parsed.Open) TryOpen(options.OutputPath, output);

        return ExitOk;
    }

    private static int RunMultiple(
        IReadOnlyList<RunGroup> groups, ReportOptions options, AttachmentSpool spool,
        Arguments parsed, TextWriter output)
    {
        var model = MultiRunBuilder.Build(groups, spool);
        ReportWriter.WriteMultiRun(model, spool, options);

        var flaky = model.Flaky.Count();
        output.WriteLine(
            $"specreport: {model.Runs.Count} runs, {model.Series.Count} scenarios, " +
            $"{flaky} flaky -> {Path.GetFullPath(options.OutputPath)}");

        if (parsed.Open) TryOpen(options.OutputPath, output);
        return ExitOk;
    }

    /// <summary>
    /// Turns command-line paths into logical runs.
    ///
    /// A DIRECTORY is one run - every .ndjson inside it merges. That is the convention that
    /// makes sharded CI work: four agents write four files into one folder, and they are one
    /// run, not four (SPEC §6.1). A FILE is its own run, so the simple case still behaves the
    /// obvious way.
    /// </summary>
    private static List<RunGroup> ResolveGroups(IReadOnlyList<string> paths)
    {
        var groups = new List<RunGroup>();

        foreach (var path in paths)
        {
            if (Directory.Exists(path))
            {
                var files = Directory
                    .EnumerateFiles(path, "*.ndjson", SearchOption.AllDirectories)
                    .OrderBy(f => f, StringComparer.Ordinal)
                    .ToList();

                if (files.Count > 0)
                    groups.Add(new RunGroup(Path.GetFileName(Path.TrimEndingDirectorySeparator(path)), files));

                continue;
            }

            foreach (var file in ExpandFiles(path))
                groups.Add(new RunGroup(Path.GetFileNameWithoutExtension(file), [file]));
        }

        return groups;
    }

    private static IEnumerable<string> ExpandFiles(string path)
    {
        if (File.Exists(path))
        {
            yield return path;
            yield break;
        }

        // Not a file or a directory: treat it as a glob relative to its own directory, so
        // `runs/*.ndjson` works even when the shell did not expand it.
        var directory = Path.GetDirectoryName(path);
        var pattern = Path.GetFileName(path);
        if (string.IsNullOrEmpty(pattern)) yield break;

        var searchIn = string.IsNullOrEmpty(directory) ? "." : directory;
        if (!Directory.Exists(searchIn)) yield break;

        foreach (var file in Directory.EnumerateFiles(searchIn, pattern)
                                      .OrderBy(f => f, StringComparer.Ordinal))
        {
            yield return file;
        }
    }

    private static void TryOpen(string path, TextWriter output)
    {
        try
        {
            Process.Start(new ProcessStartInfo(Path.GetFullPath(path)) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            output.WriteLine($"specreport: could not open the report ({ex.Message})");
        }
    }

    private static void WriteUsage(TextWriter writer) => writer.WriteLine(
        """
        specreport - HTML reports for Reqnroll runs

        USAGE
          specreport <paths...> [options]

          <paths>  .ndjson files, directories, or globs. Reqnroll writes these when the
                   `message` formatter is enabled in reqnroll.json.

        OPTIONS
          -o, --output <path>   output file (default: specreport.html)
              --title <text>    report title
              --assets <mode>   inline (default) or external
              --open            open the report when done
          -h, --help            this text

        EXIT CODES
          0  report produced (warnings included)
          2  nothing to do - no input files, or no readable envelopes
        """);
}

internal sealed record Arguments(
    IReadOnlyList<string> Paths, string Output, string? Title, bool External, bool Open)
{
    public static Arguments Parse(string[] args)
    {
        var paths = new List<string>();
        var output = "specreport.html";
        string? title = null;
        var external = false;
        var open = false;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];

            switch (arg)
            {
                case "-o" or "--output":
                    output = Next(args, ref i, "--output");
                    break;

                case "--title":
                    title = Next(args, ref i, "--title");
                    break;

                case "--assets":
                    var mode = Next(args, ref i, "--assets");
                    external = mode.Equals("external", StringComparison.OrdinalIgnoreCase);
                    if (!external && !mode.Equals("inline", StringComparison.OrdinalIgnoreCase))
                        throw new ArgumentException($"--assets must be 'inline' or 'external', not '{mode}'");
                    break;

                case "--open":
                    open = true;
                    break;

                default:
                    if (arg.StartsWith('-')) throw new ArgumentException($"unknown option '{arg}'");
                    paths.Add(arg);
                    break;
            }
        }

        return new Arguments(paths, output, title, external, open);
    }

    private static string Next(string[] args, ref int i, string option) =>
        ++i < args.Length ? args[i] : throw new ArgumentException($"{option} needs a value");
}
