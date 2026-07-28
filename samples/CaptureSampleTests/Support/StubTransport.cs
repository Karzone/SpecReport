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

        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            RequestMessage = request,
            Content = new StringContent(
                $$"""{"path":"{{request.RequestUri!.AbsolutePath}}","ok":true}""")
        };

        // A response header that must be redacted before it lands in the NDJSON.
        response.Headers.TryAddWithoutValidation("Set-Cookie", "session=super-secret-value");
        response.Headers.TryAddWithoutValidation("x-request-id", "req-0001");

        return response;
    }
}
