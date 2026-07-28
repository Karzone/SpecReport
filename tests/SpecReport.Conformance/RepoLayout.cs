namespace SpecReport.Conformance;

/// <summary>
/// Locates repo files from the test bin directory. Conformance tests assert about the
/// repository itself (csproj contents, README wording, the spec, fixtures), so they need the
/// source tree rather than just the build output.
/// </summary>
public static class RepoLayout
{
    public static string Root { get; } = FindRoot();

    public static string CriteriaPath => Path.Combine(Root, "docs", "criteria.json");
    public static string SignoffPath => Path.Combine(Root, "docs", "manual-signoff.json");
    public static string SpecPath => Path.Combine(Root, "docs", "SPEC.md");
    public static string ReadmePath => Path.Combine(Root, "README.md");
    public static string FixturesDir => Path.Combine(Root, "tests", "fixtures");
    public static string ReportPath => Path.Combine(Root, "docs", "conformance.md");

    public static string CaptureProject =>
        Path.Combine(Root, "src", "SpecReport.Capture", "SpecReport.Capture.csproj");

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "SpecReport.slnx")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            $"Could not locate the repo root (no SpecReport.slnx above '{AppContext.BaseDirectory}').");
    }
}
