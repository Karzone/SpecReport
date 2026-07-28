using System.Net;

namespace M1.HttpAttribution.Support;

/// <summary>
/// Stands in for SpecReport.Capture's DelegatingHandler. It does the one thing that matters for
/// this spike: at the moment a request passes through, work out which scenario/step it belongs
/// to - by every candidate mechanism at once - and record what each one said.
///
/// The inner handler is a stub rather than a real socket. That is deliberate: the thing under
/// test is ATTRIBUTION under async continuations and thread hops, not networking. The stub
/// still awaits a randomised delay, so continuations really do resume on arbitrary pool
/// threads, which is the condition that breaks naive ambient state.
/// </summary>
public sealed class SpikeHandler(string explicitScenarioId) : DelegatingHandler(new StubInnerHandler())
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // The request URL carries the truth: /probe/{scenarioId}/{stepId}
        var segments = request.RequestUri!.AbsolutePath.Trim('/').Split('/');
        var expectedScenario = segments.Length > 1 ? segments[1] : "?";
        var expectedStep = segments.Length > 2 ? segments[2] : "?";

        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

        // Read ambient state AFTER the await, which is where a naive mechanism falls over:
        // the continuation may be on a different thread than the one that started the request.
        var reading = AmbientProbe.Read();

        ProbeLog.Add(new ProbeLog.Observation(
            ExpectedScenario: expectedScenario,
            ExpectedStep: expectedStep,
            AsyncLocalDirect: AmbientProbe.Verdict(reading.AsyncLocalDirect, expectedScenario),
            AsyncLocalHolder: AmbientProbe.Verdict(reading.AsyncLocalHolder, expectedScenario),
            AsyncLocalSyncHook: AmbientProbe.Verdict(reading.AsyncLocalSyncHook, expectedScenario),
            ThreadStatic: AmbientProbe.Verdict(reading.ThreadStatic, expectedScenario),
            Explicit: AmbientProbe.Verdict(explicitScenarioId, expectedScenario),
            ThreadId: reading.ThreadId));

        return response;
    }
}

/// <summary>Terminates the handler chain without touching the network, but with a real
/// asynchronous gap so continuations genuinely hop threads.</summary>
internal sealed class StubInnerHandler : HttpMessageHandler
{
    private static readonly Random Rng = new(20260728);

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        int delay;
        lock (Rng) delay = Rng.Next(1, 12);

        await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        await Task.Yield();

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            RequestMessage = request,
            Content = new StringContent($$"""{"path":"{{request.RequestUri!.AbsolutePath}}"}""")
        };
    }
}
