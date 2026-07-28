using System.Text.Json;
using SpecReport.Core.Model;

namespace SpecReport.Core.Parsing;

/// <summary>
/// Streams Cucumber Messages envelopes from an NDJSON file, one line at a time.
///
/// Two properties matter more than speed:
///
/// 1. **It never throws on bad input.** A killed CI process leaves a half-written final line;
///    refusing to parse that would fail on precisely the run someone most needs a report for.
///    Unreadable lines are counted and skipped (SPEC §7.1).
/// 2. **It never holds the whole file.** A single line can be a multi-megabyte base64 image, so
///    lines are read and released one at a time rather than via ReadAllText.
/// </summary>
public static class NdjsonReader
{
    /// <summary>
    /// Yields each parseable envelope in order. The caller owns each <see cref="JsonDocument"/>
    /// and should dispose it once folded into the model - that is what keeps peak memory bounded.
    /// </summary>
    public static IEnumerable<JsonDocument> Read(string path, ParseDiagnostics diagnostics)
    {
        using var reader = new StreamReader(path);

        string? line;
        string? previous = null;

        while ((line = reader.ReadLine()) is not null)
        {
            previous = line;

            if (string.IsNullOrWhiteSpace(line)) continue;

            JsonDocument? document = null;
            try
            {
                document = JsonDocument.Parse(line);
            }
            catch (JsonException)
            {
                // Could be genuine corruption mid-file, or the truncated tail of a killed run.
                // Either way: count it, keep going. Deciding which it was happens below, once
                // we know whether this was the last line.
                diagnostics.UnreadableLines++;
            }

            if (document is not null) yield return document;
        }

        // A final line that failed to parse is the classic killed-process signature, and is
        // worth distinguishing from corruption in the middle of an otherwise complete file.
        if (previous is not null && !string.IsNullOrWhiteSpace(previous) && !IsParseable(previous))
            diagnostics.TruncatedFinalLine = true;
    }

    private static bool IsParseable(string line)
    {
        try
        {
            using var _ = JsonDocument.Parse(line);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
