using System.Net;

namespace CaptureSampleTests.Support;

/// <summary>
/// Terminates the handler chain without touching the network, but with a real asynchronous gap.
/// The thing under test is ATTRIBUTION across async continuations, not networking - and the
/// randomised delay plus Task.Yield produces genuine thread hops, which is the condition that
/// broke every naive mechanism in the M1 spike.
/// </summary>
public sealed class StubTransport : HttpMessageHandler
{
    private static readonly Random Rng = new(20260728);

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        int delay;
        lock (Rng) delay = Rng.Next(1, 12);

        await Task.Delay(delay, cancellationToken);
        await Task.Yield();

        // ?bulk=N asks for an oversized body, so body truncation is exercised end to end.
        var query = request.RequestUri!.Query;
        var bulk = 0;
        var marker = query.IndexOf("bulk=", StringComparison.Ordinal);
        if (marker >= 0) int.TryParse(query[(marker + 5)..].Split('&')[0], out bulk);

        var body = bulk > 0
            ? $$"""{"path":"{{request.RequestUri.AbsolutePath}}","filler":"{{new string('x', bulk)}}"}"""
            : $$"""{"path":"{{request.RequestUri.AbsolutePath}}","ok":true}""";

        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            RequestMessage = request,
            Content = new StringContent(body)
        };

        // A response header that must be redacted before it lands in the NDJSON.
        response.Headers.TryAddWithoutValidation("Set-Cookie", "session=super-secret-value");
        response.Headers.TryAddWithoutValidation("x-request-id", "req-0001");

        return response;
    }
}
