using System;
using System.Threading;
using System.Threading.Tasks;

namespace SpecReport.Capture;

public enum ScreenshotMode
{
    /// <summary>Never capture.</summary>
    Off,

    /// <summary>Capture only when a step fails. The default: a 200-scenario suite with a shot
    /// on every step produces a report nobody can open (SPEC §8.4).</summary>
    OnFailure,

    /// <summary>Capture after every step. Opt-in, for debugging a specific flow.</summary>
    EveryStep
}

/// <summary>
/// Produces screenshot bytes on demand.
///
/// SpecReport cannot take a screenshot itself: that needs a browser driver, and SPEC §3.5
/// forbids Capture any dependency beyond Reqnroll - referencing Playwright or Selenium here
/// would force it on every consumer, including pure API suites. So the consumer supplies a
/// one-line delegate and keeps its own driver:
///
/// <code>
/// // Playwright - ask for JPEG, which is what the size budget expects
/// capture.UseScreenshotProvider(ct =&gt; _page.ScreenshotAsync(
///     new() { Type = ScreenshotType.Jpeg, Quality = 40 }));
///
/// // Selenium
/// capture.UseScreenshotProvider(ct =&gt; Task.FromResult(
///     ((ITakesScreenshot)_driver).GetScreenshot().AsByteArray));
/// </code>
/// </summary>
public delegate Task<byte[]?> ScreenshotProvider(CancellationToken cancellationToken);

public sealed class ScreenshotOptions
{
    public ScreenshotMode Mode { get; set; } = ScreenshotMode.OnFailure;

    /// <summary>
    /// Largest screenshot accepted, in bytes. Anything bigger is DROPPED with a note rather
    /// than attached.
    ///
    /// This exists because Reqnroll inlines every attachment into the NDJSON as base64 with no
    /// size cap (measured), so an unbounded PNG stream produces an archived artifact hundreds
    /// of megabytes wide. SpecReport cannot re-compress the image itself - that needs an
    /// imaging library, and the dependency rule forbids one - so the budget is enforced at the
    /// boundary and the provider is documented to return compressed bytes (JPEG, quality ~40).
    /// See SPEC §8.4.
    /// </summary>
    public int MaxBytes { get; set; } = 512 * 1024;

    /// <summary>File extension used for the attachment, which is how Reqnroll infers media type
    /// (measured - api-notes §3). Match it to what your provider returns.</summary>
    public string FileExtension { get; set; } = ".jpg";
}

/// <summary>
/// One captured screenshot, or the reason there isn't one. Never throws: a reporting feature
/// must not fail a consumer's test run.
/// </summary>
internal sealed class ScreenshotResult
{
    // Plain private setters rather than `init`: netstandard2.0 has no IsExternalInit, and a
    // polyfill would be ceremony for two properties.
    public byte[]? Bytes { get; private set; }
    public string? Skipped { get; private set; }

    public static ScreenshotResult Captured(byte[] bytes) => new ScreenshotResult { Bytes = bytes };
    public static ScreenshotResult Reason(string why) => new ScreenshotResult { Skipped = why };

    public static async Task<ScreenshotResult> TryCaptureAsync(
        ScreenshotProvider? provider, ScreenshotOptions options, CancellationToken cancellationToken)
    {
        // No provider registered is the NORMAL state for an API suite. It is not an error, not
        // a warning, and produces nothing (SR-C7).
        if (provider is null) return Reason("no provider");

        try
        {
            var bytes = await provider(cancellationToken).ConfigureAwait(false);

            if (bytes is null || bytes.Length == 0) return Reason("provider returned nothing");

            if (bytes.Length > options.MaxBytes)
            {
                return Reason(
                    $"screenshot dropped: {bytes.Length:N0} bytes exceeds the {options.MaxBytes:N0} " +
                    $"byte budget. Return compressed bytes (JPEG quality ~40) from your provider, " +
                    $"or raise ScreenshotOptions.MaxBytes.");
            }

            return Captured(bytes);
        }
        catch (Exception ex)
        {
            // A browser that has already crashed is the common case here, and it is usually the
            // reason the step failed in the first place. Record it; never rethrow.
            return Reason($"screenshot provider threw: {ex.GetType().Name}: {ex.Message}");
        }
    }
}
