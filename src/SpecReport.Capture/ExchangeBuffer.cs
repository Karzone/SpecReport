using System.Collections.Generic;
using System.Linq;

namespace SpecReport.Capture;

/// <summary>
/// Scenario-scoped, thread-safe buffer of captured exchanges.
///
/// The handler only ever APPENDS here - it never asks "which step is this?". Attribution
/// happens later, when the [AfterStep] hook drains everything buffered since the previous
/// drain. Separating capture from attribution is what makes the design correct under
/// parallelism: the handler needs no knowledge of the Gherkin timeline at all (SPEC 3.4).
/// </summary>
public sealed class ExchangeBuffer
{
    private readonly object _gate = new object();
    private readonly List<HttpExchange> _pending = new List<HttpExchange>();
    private int _sequence;
    private bool _inStep;
    private int _stepIndex = -1;
    private string? _stepText;

    /// <summary>
    /// Whether a Gherkin step is currently executing. Toggled by the capture hooks around each
    /// step; read at APPEND time so an exchange records where it actually happened.
    ///
    /// Deciding this at drain time instead is a bug we already shipped once: a call made in
    /// BeforeScenario is still in the buffer when the first [AfterStep] fires, so it was
    /// attributed to step 1 - the exact misattribution SPEC 3.4 forbids. Scope belongs to the
    /// moment of capture, not the moment of collection.
    /// </summary>
    public bool InStep
    {
        get { lock (_gate) return _inStep; }
    }

    /// <summary>
    /// Records which step is now executing. Called from the capture hooks at each step boundary;
    /// exchanges appended afterwards carry this identity.
    /// </summary>
    public void EnterStep(int index, string? text)
    {
        lock (_gate)
        {
            _inStep = true;
            _stepIndex = index;
            _stepText = text;
        }
    }

    public void LeaveStep()
    {
        lock (_gate) _inStep = false;
    }

    public void Append(HttpExchange exchange)
    {
        lock (_gate)
        {
            exchange.Sequence = ++_sequence;

            if (_inStep)
            {
                exchange.Scope = ExchangeScope.Step;
                exchange.StepIndex = _stepIndex;
                exchange.StepText = _stepText;
            }
            else
            {
                exchange.Scope = ExchangeScope.OutsideSteps;
                exchange.StepIndex = -1;
            }

            _pending.Add(exchange);
        }
    }

    /// <summary>
    /// Removes and returns buffered exchanges of the given scope, in capture order.
    /// Step-scoped exchanges drain at each step boundary; out-of-step ones are held back until
    /// the scenario ends so they never land on an unrelated step's row.
    /// </summary>
    public IReadOnlyList<HttpExchange> Drain(ExchangeScope scope)
    {
        lock (_gate)
        {
            if (_pending.Count == 0) return System.Array.Empty<HttpExchange>();

            var drained = _pending.Where(e => e.Scope == scope).ToArray();
            if (drained.Length == 0) return System.Array.Empty<HttpExchange>();

            _pending.RemoveAll(e => e.Scope == scope);
            return drained;
        }
    }

    /// <summary>Everything still buffered, whatever its scope. Used at scenario teardown so
    /// nothing is silently discarded.</summary>
    public IReadOnlyList<HttpExchange> DrainAll()
    {
        lock (_gate)
        {
            if (_pending.Count == 0) return System.Array.Empty<HttpExchange>();

            var drained = _pending.ToArray();
            _pending.Clear();
            return drained;
        }
    }

    public int PendingCount
    {
        get { lock (_gate) return _pending.Count; }
    }
}
