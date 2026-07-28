namespace SpecReport.Core.Model;

/// <summary>
/// What the parser could not make sense of.
///
/// The most valuable report anyone generates is for the run the CI timeout killed - truncated
/// final line, no `testRunFinished`, scenarios that started and never finished. Refusing to
/// parse that would fail exactly when the tool matters most, so parsing is best-effort and the
/// damage is REPORTED rather than swallowed (SPEC §7.1).
/// </summary>
public sealed class ParseDiagnostics
{
    /// <summary>Lines that were not valid JSON. Counted, not fatal.</summary>
    public int UnreadableLines { get; set; }

    /// <summary>The final line was cut mid-write - the classic killed-process signature.</summary>
    public bool TruncatedFinalLine { get; set; }

    /// <summary>No `testRunFinished` envelope: the run did not complete.</summary>
    public bool RunDidNotComplete { get; set; }

    /// <summary>Scenarios that started but never reported a finish.</summary>
    public int UnfinishedScenarios { get; set; }

    /// <summary>Envelope types this build doesn't know. Ignored on purpose so a future Reqnroll
    /// release doesn't break SpecReport; recorded so the cause of any oddity is visible.</summary>
    public ISet<string> UnknownEnvelopeTypes { get; } =
        new SortedSet<string>(StringComparer.Ordinal);

    /// <summary>`meta.protocolVersion` differing from what this build was written against.
    /// A warning, never an error.</summary>
    public string? ProtocolVersionWarning { get; set; }

    public bool AnyDegradation =>
        UnreadableLines > 0 || TruncatedFinalLine || RunDidNotComplete ||
        UnfinishedScenarios > 0 || ProtocolVersionWarning is not null;

    /// <summary>One line for the report header banner. Null when the run parsed cleanly.</summary>
    public string? Summary()
    {
        if (!AnyDegradation) return null;

        var parts = new List<string>();

        if (RunDidNotComplete) parts.Add("the run did not complete");
        if (TruncatedFinalLine) parts.Add("the last line was truncated");
        if (UnreadableLines > 0) parts.Add($"{UnreadableLines} unreadable line(s) skipped");
        if (UnfinishedScenarios > 0) parts.Add($"{UnfinishedScenarios} scenario(s) never finished");
        if (ProtocolVersionWarning is not null) parts.Add(ProtocolVersionWarning);

        return string.Join("; ", parts);
    }
}
