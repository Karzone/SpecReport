using System;
using System.Collections.Generic;

namespace SpecReport.Capture;

/// <summary>Where a captured exchange sits relative to the Gherkin timeline.</summary>
public enum ExchangeScope
{
    /// <summary>Made while a step was executing - attaches to that step.</summary>
    Step,

    /// <summary>Made outside any step (Before/AfterScenario hooks, background polling).
    /// Rendered at scenario level rather than being dropped or blamed on an arbitrary step
    /// (SPEC 3.4).</summary>
    OutsideSteps
}

public sealed class CaptureOptions
{
    /// <summary>
    /// Header names redacted outright. Matching is case-insensitive and exact.
    /// Redaction happens at CAPTURE time because the NDJSON is archived as a CI artifact -
    /// a secret that reaches the file has already leaked (SPEC 8.1).
    /// </summary>
    public ISet<string> RedactedHeaders { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Authorization", "Proxy-Authorization", "Cookie", "Set-Cookie",
            "X-API-Key", "X-Api-Token", "X-Auth-Token"
        };

    /// <summary>
    /// Substrings that mark a header or query parameter as sensitive wherever they appear.
    /// Case-insensitive.
    /// </summary>
    public IList<string> SensitiveNameFragments { get; } = new List<string>
    {
        "token", "secret", "password", "passwd", "apikey", "api-key", "authorization", "session"
    };

    /// <summary>Maximum bytes captured per body. Larger bodies are truncated with a visible
    /// marker rather than silently trimmed (SPEC 8.3).</summary>
    public int MaxBodyBytes { get; set; } = 64 * 1024;

    /// <summary>Capture request/response bodies at all. Headers and timing are always captured.</summary>
    public bool CaptureBodies { get; set; } = true;

    /// <summary>Replacement text for redacted values. Visible on purpose - the report must show
    /// that something was hidden, not hide that it hid something.</summary>
    public string RedactionPlaceholder { get; set; } = "*** redacted ***";

    /// <summary>
    /// Optional last-pass scrubber for body content (PII, tenant identifiers - things only the
    /// consuming team can recognise). Applied after built-in redaction.
    /// </summary>
    public Func<string, string>? BodyScrubber { get; set; }

    public static CaptureOptions Default { get; } = new CaptureOptions();
}
