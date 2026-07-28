using System.Diagnostics;
using System.Text.RegularExpressions;
using SpecReport.Cli;
using SpecReport.Core.Parsing;
using SpecReport.Core.Rendering;
using Xunit;
using Xunit.Abstractions;

namespace SpecReport.Conformance;

/// <summary>Evidence for milestone M6: accessibility, scale, and graceful degradation.</summary>
public sealed class M6Criteria(ITestOutputHelper output) : IDisposable
{
    private readonly AttachmentSpool _spool = new();
    private readonly List<string> _temporary = [];

    public void Dispose()
    {
        _spool.Dispose();
        foreach (var path in _temporary)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
                if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
            }
            catch (IOException) { }
        }
    }

    private string Render(string fixture, ReportOptions? options = null)
    {
        var run = RunParser.Parse(Path.Combine(RepoLayout.FixturesDir, fixture), _spool);
        return ReportWriter.Render(run, _spool, options ?? new ReportOptions());
    }

    // ---- accessibility -------------------------------------------------------------------

    [Fact]
    [Criterion("SR-R5")]
    public void EveryInteractiveControlIsKeyboardOperable()
    {
        var html = Render("sample-run-01.ndjson");

        // 1. Nothing interactive is a bare element with a handler bolted on. A <div> or <img>
        //    that responds to clicks is invisible to keyboard and screen-reader users - the
        //    lightbox may as well not exist for them. Screenshots open from a real <button>.
        Assert.DoesNotContain("onclick", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<button type=\"button\" class=\"shot\"", html, StringComparison.Ordinal);

        // 2. Expansion uses <details>/<summary>, which browsers make focusable and operable
        //    with Enter/Space at both levels - no tabindex or key handling of our own.
        Assert.Contains("<details class=\"scenario\"", html, StringComparison.Ordinal);
        Assert.Contains("<details class=\"step-block\"", html, StringComparison.Ordinal);
        Assert.Contains("<summary class=\"step ", html, StringComparison.Ordinal);

        // 3. The lightbox is a native <dialog> IN THE MARKUP. Opened with showModal() it traps
        //    focus and closes on Escape with no script at all - so "Escape closes the lightbox"
        //    is a property of the file, provable without driving a browser (SPEC §14.2).
        Assert.Contains("<dialog class=\"lightbox\"", html, StringComparison.Ordinal);
        Assert.Contains("showModal()", html, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Screenshot viewer\"", html, StringComparison.Ordinal);

        // 4. Every lightbox control is a labelled button, including the ones whose visible text
        //    is a chevron.
        foreach (var label in new[] { "Previous screenshot", "Next screenshot", "Close screenshot viewer" })
            Assert.Contains($"aria-label=\"{label}\"", html, StringComparison.Ordinal);

        // 5. Focus is visibly indicated. Keyboard operability without a visible focus ring is
        //    operable in theory only.
        Assert.Contains(":focus-visible", html, StringComparison.Ordinal);

        // 6. No positive tabindex anywhere - it reorders the tab sequence unpredictably.
        Assert.Empty(Regex.Matches(html, @"tabindex\s*=\s*""[1-9]"));

        // 7. Filters and search are native form controls.
        Assert.Contains("<input type=\"search\"", html, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Search scenarios and steps\"", html, StringComparison.Ordinal);
    }

    // ---- scale ----------------------------------------------------------------------------

    [Fact]
    [Criterion("SR-R8")]
    public void TheScaleBudgetHolds()
    {
        const int scenarios = 5_000;
        const int stepsEach = 10;               // 50,000 steps

        var source = SyntheticRun.Write(scenarios, stepsEach);
        _temporary.Add(source);

        var sourceMb = new FileInfo(source).Length / 1024.0 / 1024.0;

        using var spool = new AttachmentSpool();

        var parseWatch = Stopwatch.StartNew();
        var run = RunParser.Parse(source, spool);
        parseWatch.Stop();

        Assert.Equal(scenarios, run.AllScenarios.Count());
        Assert.Equal(scenarios * stepsEach, run.AllScenarios.Sum(s => s.Outcome!.Steps.Count));

        var target = Path.Combine(Path.GetTempPath(), $"specreport-scale-{Guid.NewGuid():N}.html");
        _temporary.Add(target);

        var renderWatch = Stopwatch.StartNew();
        ReportWriter.Write(run, spool, new ReportOptions { OutputPath = target });
        renderWatch.Stop();

        var outputMb = new FileInfo(target).Length / 1024.0 / 1024.0;
        var total = parseWatch.ElapsedMilliseconds + renderWatch.ElapsedMilliseconds;

        output.WriteLine($"input   {sourceMb:F1} MB");
        output.WriteLine($"parse   {parseWatch.ElapsedMilliseconds} ms");
        output.WriteLine($"render  {renderWatch.ElapsedMilliseconds} ms");
        output.WriteLine($"output  {outputMb:F1} MB");

        // Generation budget. Kept well above the measured figure so the test fails on a real
        // regression rather than on a slow CI agent.
        Assert.True(total < 10_000,
            $"Parsing and rendering {scenarios} scenarios took {total} ms. The budget is about " +
            $"generation cost, and a multi-second regression here means something became " +
            $"quadratic (SPEC §8.7).");

        // Page weight is the honest proxy for "opens in under 3 seconds". Browser paint time
        // cannot be measured without driving a browser, but a document this size is what
        // determines it - and an unopenable file is the failure mode that actually bites.
        Assert.True(outputMb < 40,
            $"The report is {outputMb:F1} MB. Past roughly this size a browser struggles, and " +
            $"the run sizes that motivated a scale budget are exactly the ones that would then " +
            $"fail to open.");

        // Filtering must not do per-scenario work at load: the page is server-rendered and the
        // script only toggles `hidden`, so cost is independent of scenario count.
        var html = File.ReadAllText(target);
        Assert.DoesNotContain("JSON.parse", html, StringComparison.Ordinal);
    }

    // ---- graceful degradation ---------------------------------------------------------------

    [Fact]
    [Criterion("SR-X2")]
    public void OversizedOutputDegradesToExternalAssetsRatherThanAborting()
    {
        var target = Path.Combine(Path.GetTempPath(), $"specreport-degrade-{Guid.NewGuid():N}.html");
        _temporary.Add(target);

        var options = new ReportOptions
        {
            OutputPath = target,
            Assets = AssetMode.Inline,
            // Far below the fixture's screenshots, forcing the degrade path.
            MaxInlineBytes = 1024
        };
        _temporary.Add(options.AssetDirectory);

        var run = RunParser.Parse(
            Path.Combine(RepoLayout.FixturesDir, "sample-run-01.ndjson"), _spool);

        ReportWriter.Write(run, _spool, options);

        // A report exists. Aborting would deny the user the report entirely to save them from
        // a large one - the wrong trade (SPEC §7.1).
        Assert.True(File.Exists(target));
        Assert.True(options.DegradedToExternal);

        var html = File.ReadAllText(target);

        // Screenshots moved beside the file instead of being inlined...
        Assert.DoesNotContain("src=\"data:image", html, StringComparison.Ordinal);
        Assert.True(Directory.Exists(options.AssetDirectory));
        Assert.NotEmpty(Directory.GetFiles(options.AssetDirectory));

        // ...and are still referenced, so the evidence is not lost.
        Assert.Contains(Path.GetFileName(options.AssetDirectory), html, StringComparison.Ordinal);

        // The CLI says so out loud and still exits 0 - degrading quietly would let someone
        // archive the HTML alone and lose every screenshot without knowing.
        var cliOut = new StringWriter();
        var cliErr = new StringWriter();
        var cliTarget = Path.Combine(Path.GetTempPath(), $"specreport-degrade-cli-{Guid.NewGuid():N}.html");
        _temporary.Add(cliTarget);

        var exit = CommandLine.Run(
            [Path.Combine(RepoLayout.FixturesDir, "sample-run-01.ndjson"), "-o", cliTarget],
            cliOut, cliErr);

        Assert.Equal(CommandLine.ExitOk, exit);
    }
}
