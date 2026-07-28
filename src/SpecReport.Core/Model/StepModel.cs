namespace SpecReport.Core.Model;

public enum StepKind
{
    /// <summary>A step written in the feature file.</summary>
    Gherkin,

    /// <summary>A Before/After hook. Modelled as a first-class row because a failing
    /// BeforeScenario produces a failed scenario with ZERO failed Gherkin steps - without hook
    /// rows the report would show "everything skipped" and no error anywhere (SPEC §4.3).</summary>
    Hook
}

public enum HookKind
{
    Unknown,
    BeforeTestRun,
    AfterTestRun,
    BeforeTestCase,
    AfterTestCase,
    BeforeTestStep,
    AfterTestStep
}

public sealed class StepModel
{
    public StepKind Kind { get; init; } = StepKind.Gherkin;

    /// <summary>
    /// The literal Gherkin keyword with its trailing space trimmed - "Given", "When",
    /// "Angenommen". Comes from the gherkinDocument AST: pickle steps carry text but NO keyword
    /// (measured), which is why the AST join is mandatory rather than an optimisation.
    /// </summary>
    public string Keyword { get; init; } = string.Empty;

    /// <summary>
    /// Context / Action / Outcome / Conjunction. **This is the styling key**, never the keyword
    /// string: comparing against "Given"/"When"/"Then" breaks the moment a feature is written in
    /// German, and localized Gherkin is supported (SPEC §4.3).
    /// </summary>
    public string KeywordType { get; init; } = string.Empty;

    public string Text { get; init; } = string.Empty;

    /// <summary>Came from the feature's Background block. Pickles inline Background steps, so
    /// without this flag they read as anonymous leading steps in every scenario.</summary>
    public bool IsBackground { get; init; }

    public StepStatus Status { get; init; } = StepStatus.Unknown;

    /// <summary>Reported by the run, or inferred by the parser. Inference is a fallback for
    /// truncated streams only, and is marked so it can be rendered distinctly (SPEC §4.4).</summary>
    public StatusSource StatusSource { get; init; } = StatusSource.Reported;

    public TimeSpan Duration { get; init; }

    public StepError? Error { get; init; }

    public DataTable? DataTable { get; init; }
    public string? DocString { get; init; }

    /// <summary>Screenshots, HTTP exchanges, logs, files - dispatched by type at render time.</summary>
    public IReadOnlyList<Evidence> Evidence { get; init; } = [];

    /// <summary>
    /// Position among this scenario's Gherkin steps, or -1 for hook rows. Capture stamps the
    /// same ordinal into its evidence payloads, which is how evidence attached to a dropped
    /// per-step hook finds its way back to the step that produced it (api-notes §10).
    /// </summary>
    public int Ordinal { get; init; } = -1;

    internal StepModel WithEvidence(IReadOnlyList<Evidence> evidence) => new()
    {
        Kind = Kind,
        Keyword = Keyword,
        KeywordType = KeywordType,
        Text = Text,
        IsBackground = IsBackground,
        Status = Status,
        StatusSource = StatusSource,
        Duration = Duration,
        Error = Error,
        DataTable = DataTable,
        DocString = DocString,
        Evidence = evidence,
        Ordinal = Ordinal,
        HookKind = HookKind,
        HookTagExpression = HookTagExpression
    };

    // --- Hook rows only -------------------------------------------------------------------

    public HookKind HookKind { get; init; } = HookKind.Unknown;

    /// <summary>The hook's tag scope, e.g. "@fail-before-hook". Part of the row label because
    /// the `hook` envelope's `name` is always EMPTY (measured - api-notes §7.1), so there is no
    /// method name to show.</summary>
    public string? HookTagExpression { get; init; }

    /// <summary>Label for a hook row, built from what the stream actually carries.</summary>
    public string HookLabel
    {
        get
        {
            var phase = HookKind switch
            {
                HookKind.BeforeTestRun => "Before test run",
                HookKind.AfterTestRun => "After test run",
                HookKind.BeforeTestCase => "Before hook",
                HookKind.AfterTestCase => "After hook",
                HookKind.BeforeTestStep => "Before step hook",
                HookKind.AfterTestStep => "After step hook",
                _ => "Hook"
            };

            return string.IsNullOrWhiteSpace(HookTagExpression)
                ? phase
                : $"{phase} ({HookTagExpression})";
        }
    }

    /// <summary>What a timeline row shows, whichever kind it is.</summary>
    public string DisplayText => Kind == StepKind.Hook ? HookLabel : Text;
}

/// <summary>
/// A step failure. `message` on the result is only the exception message; the stack trace lives
/// on `exception` (measured), so both are kept.
/// </summary>
public sealed class StepError
{
    public string? Type { get; init; }
    public string? Message { get; init; }
    public string? StackTrace { get; init; }
}

/// <summary>A Gherkin data table. The first row is the header; Cucumber does not separate it.</summary>
public sealed class DataTable
{
    public IReadOnlyList<IReadOnlyList<string>> Rows { get; init; } = [];

    public IReadOnlyList<string> Header => Rows.Count > 0 ? Rows[0] : [];
    public IEnumerable<IReadOnlyList<string>> Body => Rows.Skip(1);
}
