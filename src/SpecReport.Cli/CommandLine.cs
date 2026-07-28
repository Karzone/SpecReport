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

        var inputs = ResolveInputs(parsed.Paths).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        if (inputs.Count == 0)
        {
            error.WriteLine(
                "specreport: no .ndjson files found. Enable Reqnroll's `message` formatter in " +
                "reqnroll.json, then point this at the file it writes.");
            return ExitNothingToDo;
        }

        // v1 renders a single run; merging is M5. Extra files are REPORTED rather than
        // silently ignored - quiet truncation reads as "covered everything" when it did not.
        if (inputs.Count > 1)
        {
            error.WriteLine(
                $"specreport: {inputs.Count} files given; rendering the first ({inputs[0]}). " +
                $"Multi-run reports arrive in a later release.");
        }

        var options = new ReportOptions
        {
            OutputPath = parsed.Output,
            Title = parsed.Title,
            Assets = parsed.External ? AssetMode.External : AssetMode.Inline
        };

        using var spool = new AttachmentSpool();
        var run = RunParser.Parse(inputs[0], spool);
        var scenarioCount = run.AllScenarios.Count();

        if (scenarioCount == 0 && run.Diagnostics.UnreadableLines > 0)
        {
            error.WriteLine(
                $"specreport: '{inputs[0]}' contained no readable envelopes " +
                $"({run.Diagnostics.UnreadableLines} unreadable line(s)).");
            return ExitNothingToDo;
        }

        ReportWriter.Write(run, spool, options);

        output.WriteLine(
            $"specreport: {scenarioCount} scenario(s) -> {Path.GetFullPath(options.OutputPath)}");

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

    private static IEnumerable<string> ResolveInputs(IReadOnlyList<string> paths)
    {
        foreach (var path in paths)
        {
            if (File.Exists(path))
            {
                yield return path;
                continue;
            }

            if (Directory.Exists(path))
            {
                foreach (var file in Directory
                             .EnumerateFiles(path, "*.ndjson", SearchOption.AllDirectories)
                             .OrderBy(f => f, StringComparer.Ordinal))
                {
                    yield return file;
                }
                continue;
            }

            // Not a file or directory: treat it as a glob relative to its own directory.
            var directory = Path.GetDirectoryName(path);
            var pattern = Path.GetFileName(path);
            if (string.IsNullOrEmpty(pattern)) continue;

            var searchIn = string.IsNullOrEmpty(directory) ? "." : directory;
            if (!Directory.Exists(searchIn)) continue;

            foreach (var file in Directory.EnumerateFiles(searchIn, pattern)
                                          .OrderBy(f => f, StringComparer.Ordinal))
            {
                yield return file;
            }
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
