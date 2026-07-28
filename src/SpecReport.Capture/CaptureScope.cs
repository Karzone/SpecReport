using System;
using System.Threading;

namespace SpecReport.Capture;

/// <summary>
/// Resolves the current scenario's <see cref="ExchangeBuffer"/> for handlers that were not
/// given one explicitly - i.e. clients built once at startup by IHttpClientFactory rather than
/// per scenario.
///
/// MECHANISM CHOICE IS LOAD-BEARING AND WAS SETTLED BY MEASUREMENT, NOT REASONING.
/// See spikes/M1.HttpAttribution/FINDINGS.md - 32 observations x 5 runs, 8-way parallelism:
///
///   AsyncLocal seeded in an ASYNC hook ....... ABSENT 32/32   (copy-on-write ExecutionContext)
///   AsyncLocal + mutable holder (async hook) . ABSENT 32/32   (the reference is lost too)
///   [ThreadStatic] ........................... 6-20 WRONG     (ANOTHER scenario's buffer!)
///   AsyncLocal seeded in a SYNCHRONOUS hook .. CORRECT 32/32
///
/// Two rules follow, and neither is stylistic:
///
/// 1. <see cref="Enter"/> MUST be called from a SYNCHRONOUS hook. A sync method has no async
///    state machine of its own, so the assignment lands on a context the steps inherit.
///    Making that hook `async Task` silently reverts capture to ABSENT - it will not throw,
///    tests will still pass, and the report will simply have no HTTP evidence. There is a
///    guard test for this precisely because the failure is invisible.
///
/// 2. Thread-affine state ([ThreadStatic], thread-id keyed dictionaries) is FORBIDDEN here.
///    It does not merely lose the value - it returns a DIFFERENT scenario's buffer, producing
///    a plausible-looking report that is quietly wrong, with a different wrong split each run.
/// </summary>
public static class CaptureScope
{
    private static readonly AsyncLocal<ExchangeBuffer?> Current = new AsyncLocal<ExchangeBuffer?>();

    /// <summary>
    /// Binds a buffer to the current execution context. MUST be called from a synchronous hook
    /// - see the type remarks.
    /// </summary>
    public static void Enter(ExchangeBuffer buffer)
    {
        if (buffer is null) throw new ArgumentNullException(nameof(buffer));
        Current.Value = buffer;
    }

    public static void Exit() => Current.Value = null;

    /// <summary>
    /// The current scenario's buffer, or null outside a scenario. Null is a legitimate state
    /// (a client used before any scenario starts), so callers drop the exchange rather than
    /// throwing - failing a consumer's test run because our reporting could not attribute
    /// something would be a poor trade.
    /// </summary>
    public static ExchangeBuffer? Buffer => Current.Value;
}
