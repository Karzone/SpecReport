using System;
using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SpecReport.Capture;

/// <summary>
/// Captures HTTP traffic passing through it and appends it to the current scenario's buffer.
/// Attach it to any HttpClient - it works with every client library built on HttpClient
/// (Refit, RestSharp v107+, Flurl, typed clients), which is why SpecReport needs no
/// per-library integration (SPEC 3.5).
///
/// Two ways to give it a buffer:
///   - EXPLICIT (recommended): pass one in, built per scenario. Correct by construction.
///   - AMBIENT: leave it null and let <see cref="CaptureScope"/> resolve it, for clients built
///     once at startup by IHttpClientFactory.
///
/// The handler never asks which STEP it is in. It only appends; the [AfterStep] hook attributes.
/// </summary>
public sealed class SpecReportHttpHandler : DelegatingHandler
{
    private readonly ExchangeBuffer? _explicitBuffer;
    private readonly CaptureOptions _options;

    /// <summary>Ambient mode - the buffer is resolved per request from <see cref="CaptureScope"/>.</summary>
    public SpecReportHttpHandler(CaptureOptions? options = null)
        : this(null, options)
    {
    }

    /// <summary>Explicit mode - this handler always writes to the given buffer.</summary>
    public SpecReportHttpHandler(ExchangeBuffer? buffer, CaptureOptions? options = null)
    {
        _explicitBuffer = buffer;
        _options = options ?? CaptureOptions.Default;
    }

    public SpecReportHttpHandler(HttpMessageHandler innerHandler, ExchangeBuffer? buffer = null,
                                 CaptureOptions? options = null)
        : base(innerHandler)
    {
        _explicitBuffer = buffer;
        _options = options ?? CaptureOptions.Default;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var buffer = _explicitBuffer ?? CaptureScope.Buffer;

        // No scenario in scope - nothing to attribute to. Pass through untouched rather than
        // failing the consumer's request because our reporting had nowhere to put it.
        if (buffer is null)
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

        var exchange = new HttpExchange
        {
            StartedAt = DateTimeOffset.UtcNow,
            Method = request.Method.Method
        };

        var redactions = exchange.Redactions;
        exchange.Url = Redaction.RedactUrl(
            request.RequestUri?.ToString() ?? string.Empty, _options, redactions);
        exchange.RequestHeaders = Redaction.RedactHeaders(
            request.Headers, "request", _options, redactions);

        if (request.Content != null)
        {
            foreach (var h in Redaction.RedactHeaders(
                         request.Content.Headers, "request", _options, redactions))
            {
                exchange.RequestHeaders[h.Key] = h.Value;
            }

            if (_options.CaptureBodies)
                await ReadBodyAsync(request.Content, exchange, isRequest: true).ConfigureAwait(false);
        }

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();

            exchange.DurationMs = stopwatch.Elapsed.TotalMilliseconds;
            exchange.Status = (int)response.StatusCode;
            exchange.Reason = response.ReasonPhrase;
            exchange.ResponseHeaders = Redaction.RedactHeaders(
                response.Headers, "response", _options, redactions);

            if (response.Content != null)
            {
                foreach (var h in Redaction.RedactHeaders(
                             response.Content.Headers, "response", _options, redactions))
                {
                    exchange.ResponseHeaders[h.Key] = h.Value;
                }

                if (_options.CaptureBodies)
                    await ReadBodyAsync(response.Content, exchange, isRequest: false).ConfigureAwait(false);
            }

            buffer.Append(exchange);
            return response;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();

            // A failed request is often the most interesting thing in the report, so record it
            // and rethrow untouched - the caller's error handling must see exactly what it
            // would have seen without us.
            exchange.DurationMs = stopwatch.Elapsed.TotalMilliseconds;
            exchange.Error = ex.GetType().Name + ": " + ex.Message;
            buffer.Append(exchange);
            throw;
        }
    }

    /// <summary>
    /// Buffers a body for capture. Content is re-materialised as a ByteArrayContent so the
    /// caller can still read it - reading a stream content without this would consume it and
    /// break the system under test.
    /// </summary>
    private async Task ReadBodyAsync(HttpContent content, HttpExchange exchange, bool isRequest)
    {
        byte[] bytes;

        try
        {
            bytes = await content.ReadAsByteArrayAsync().ConfigureAwait(false);
        }
        catch
        {
            // Streaming or already-consumed content. Headers and timing still make the
            // exchange worth reporting; the body is simply marked absent.
            return;
        }

        var truncated = bytes.Length > _options.MaxBodyBytes;
        var text = Encoding.UTF8.GetString(bytes, 0, truncated ? _options.MaxBodyBytes : bytes.Length);

        // Redact BEFORE the text reaches the exchange. A login response whose body is a raw
        // JWT is the most common API test there is, and the NDJSON gets archived as a CI
        // artifact - a secret that reaches the file has already leaked (SPEC §8.1).
        if (_options.RedactJsonBodies && LooksLikeJson(text))
        {
            text = JsonRedactor.Redact(
                text, isRequest ? "request" : "response", _options, exchange.Redactions);
        }

        if (_options.BodyScrubber != null)
            text = _options.BodyScrubber(text);

        if (isRequest)
        {
            exchange.RequestBody = text;
            exchange.RequestBodyTruncated = truncated;
            exchange.RequestBodyBytes = bytes.Length;
        }
        else
        {
            exchange.ResponseBody = text;
            exchange.ResponseBodyTruncated = truncated;
            exchange.ResponseBodyBytes = bytes.Length;
        }
    }

    /// <summary>Cheap shape check - running the redactor over a JPEG would be pointless work.</summary>
    private static bool LooksLikeJson(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (char.IsWhiteSpace(c)) continue;
            return c == '{' || c == '[';
        }

        return false;
    }
}
