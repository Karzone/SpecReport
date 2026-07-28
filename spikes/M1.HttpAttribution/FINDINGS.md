# M1 spike — HTTP attribution under parallel execution

**Question:** a `DelegatingHandler` lives in the system-under-test's DI world, not Reqnroll's.
When it captures an exchange, how does it find the *current scenario's* buffer — correctly,
under parallel execution with async steps?

**Method:** 8 scenarios run concurrently (MSTest method-level parallelism), each making 4 async
HTTP calls through the handler. Every request URL encodes the scenario and step it belongs to,
so each candidate mechanism can be scored against ground truth at the moment of capture — and
crucially, *after* an `await`, where continuations resume on arbitrary pool threads.

32 observations per run, 5 runs, 8–11 distinct threads each time.

## Result

| Mechanism | Verdict | Outcome |
|---|---|---|
| `AsyncLocal` seeded in an **async** `[BeforeScenario]` | ❌ broken | **ABSENT 32/32**, every run |
| `AsyncLocal<Holder>` (mutable holder) seeded in an async hook | ❌ broken | **ABSENT 32/32**, every run |
| `[ThreadStatic]` | ☠️ **dangerous** | 6–20 **WRONG**, 1–4 correct, rest absent — *and the split differs every run* |
| `AsyncLocal` seeded in a **synchronous** `[BeforeScenario]` | ✅ works | **CORRECT 32/32**, all 5 runs |
| Handler constructed **per scenario** with the buffer injected | ✅ works | **CORRECT 32/32**, all 5 runs |

## What this means

**1. The async-hook `AsyncLocal` trap is real, and it fails safe.**
Assigning `AsyncLocal<T>.Value` inside an async hook mutates that method's copy-on-write
`ExecutionContext`, which is discarded when the hook returns. The mutable-holder workaround
does not help: the *reference* assignment is lost the same way. Both read back as `null` — bad,
but loudly bad.

**2. `[ThreadStatic]` fails *unsafely*, which is worse.**
It doesn't merely lose the value — it returns **another scenario's** value 6–20 times out of 32,
because async continuations resume on pool threads that other scenarios have already touched.
That is silent cross-scenario contamination: HTTP traffic attributed to a step that never made
the call, in a report that looks perfectly plausible. The proportion varies run to run, so a
green smoke test proves nothing. **Never use thread-affine state here.**

**3. Two mechanisms are correct, and we ship both.**
- *Explicit construction* — the handler is built per scenario holding its buffer directly. No
  ambient lookup at all, correct by construction. This is the recommended path.
- *`AsyncLocal` seeded from a **synchronous** hook* — works because a sync method has no async
  state machine of its own, so the assignment lands on a context the steps inherit. This is what
  makes `IHttpClientFactory` consumers (whose clients are built once at startup, not per
  scenario) supportable at all.

The second finding is the one that mattered: without it, factory-built clients would have been
unsupportable and the spec's §3.4 ambient design would have been a dead end.

## Consequences for the design

- SPEC §3.4 rewritten: explicit construction primary, sync-hook `AsyncLocal` for ambient mode.
- **The seeding hook must be synchronous.** This is a correctness requirement, not style — and
  it is exactly the kind of thing a future refactor would "clean up" into `async Task` and
  silently break. It needs a guard test, not just a comment.
- The mutable-holder idea from the original §3.4 is **dropped**: it solves a problem that
  doesn't exist and doesn't solve the one that does.

## Honest limitations of this spike

- The inner handler is a **stub**, not a real socket. Deliberate: the thing under test is
  attribution across async continuations, not networking. A stub with randomised delays plus
  `Task.Yield()` produces genuine thread hops (8–11 distinct threads for 8 scenarios), which is
  the condition that breaks naive mechanisms — as the ThreadStatic column demonstrates.
- Measured on **MSTest** method-level parallelism only. NUnit and xUnit schedule differently.
  The mechanisms that *fail* here will not start working elsewhere, but the sync-hook mechanism
  should be re-measured before claiming NUnit/xUnit support.
- 8 concurrent scenarios on this machine. Higher concurrency might surface more contention; it
  cannot make a broken mechanism correct.
