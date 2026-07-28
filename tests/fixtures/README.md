# Fixtures

Recorded Cucumber Messages streams used by `SpecReport.Core.Tests`. These are **real output**
from a real Reqnroll run, not hand-written approximations — the whole point is that the parser
is tested against bytes Reqnroll actually produces.

## sample-run-01.ndjson

| | |
|---|---|
| Source | `samples/SampleTests` |
| Recorded | 2026-07-28 |
| Reqnroll | 3.3.4 (MSTest), protocolVersion 30.1.0 |
| Runtime | .NET 10.0.8, Windows x64 |
| Size | ~445 KB, 505 lines |
| Outcome | 28 scenarios: 19 passed, 7 failed, 1 pending, 1 undefined |

**The 7 failures are deliberate.** The sample exists to produce a realistic stream, not to go
green. Every failing scenario is tagged (`@known-fail`, `@fail-before-hook`, `@fail-after-hook`)
or named "...that fails on purpose". If you ever see an *8th* failure, the sample is broken —
not the fixture.

### Coverage

| Case | Where |
|---|---|
| Passing / failing scenarios | `Basics.feature` |
| Skipped-after-failure | `Basics.feature` — "A failing assertion, with steps skipped after it" |
| Pending + undefined steps | `Basics.feature` (also produces the `suggestion` envelope) |
| Background | `Basics.feature`, `Rules.feature`, `Localised.feature` |
| Data table + doc string | `Basics.feature` |
| Scenario Outline + Examples | `Outlines.feature` — incl. two **identical** example rows |
| Gherkin `Rule:` | `Rules.feature` — two rules, one with its own Background |
| Failing Before / After hooks | `Hooks.feature` — failed scenario with **zero** failed steps |
| Screenshot attachment (`image/png`) | `Evidence.feature` |
| HTTP evidence (`$specreport: http/1`) | `Evidence.feature` — success, failure, and 3-on-one-step |
| Undiscriminated JSON attachment | `Evidence.feature` — must degrade to a plain JSON view |
| Text output (`text/x.cucumber.log+plain`) | `Evidence.feature` |
| Localized Gherkin (German) | `Localised.feature` |

### Regenerating

```bash
dotnet test samples/SampleTests/SampleTests.csproj
```

then copy `samples/SampleTests/bin/Debug/net10.0/specreport-messages.ndjson` over the fixture.
**Expect a diff on every regeneration**: execution ids (`testCase.id`, `testCaseStarted.id`,
`testStepId`), timestamps, and durations are all per-run. Gherkin-derived ids (`pickle.id`,
AST node ids) are content hashes and stay stable — see `docs/reqnroll-api-notes.md` §6.

Regenerate only when the sample's features change or Reqnroll is upgraded, and say which in the
commit message.

### Still to record (later milestones)

- A **truncated** stream (kill the test host mid-run) — SPEC §7.1 best-effort parsing.
- A **parallel** run — to confirm whether `testCaseStarted.workerId` gets populated.
- A **synthetic 5k-scenario** stream — SPEC §8.7 scale budget.
- A **sharded** pair of streams — SPEC §6.1 directory-as-one-run merging.

---

## capture-run-02.ndjson

| | |
|---|---|
| Source | `samples/CaptureSampleTests` (consumes `SpecReport.Capture`) |
| Recorded | 2026-07-28 |
| Size | ~145 KB, 417 lines |
| Outcome | 9 scenarios, all passing, 42 captured HTTP exchanges |

Proof fixture for M1 (SR-H1/SR-H2). Eight scenarios run concurrently under MSTest
method-level parallelism, each making four async calls plus one from a `BeforeScenario`
hook. **Every request URL encodes the step it was made from**, so the fixture carries its own
ground truth: 33 step-scoped exchanges whose `stepText` must match their URL token, and 9
`outside-steps` exchanges that must never be attributed to a Gherkin step.

It also plants two real secrets (a bearer token and an `api_key` query parameter) plus a
`Set-Cookie` response header, so redaction can be proven by their **absence** rather than by
grepping for the word "redacted".

---

## truncated-run-03.ndjson

The killed-CI-run case (SPEC §7.1). `sample-run-01` cut at 72% with the final line left
half-written: no `testRunFinished`, scenarios that started and never finished, one unparseable
tail line. Proves the parser degrades honestly instead of refusing the run someone most needs a
report for.

## retry-run-04.ndjson

**Hand-authored, not recorded** — Reqnroll will not produce a retry on demand, and SR-P8 is
about parsing semantics rather than Reqnroll behaviour. Two `testCaseStarted` envelopes share
one `testCaseId`: attempt 0 fails, attempt 1 passes. The scenario must count once, take the
last attempt as its outcome, and register as flaky-within-run.

It also carries a made-up envelope type and unknown properties on known envelopes, so
forward-compatibility (SR-P11) is proven rather than assumed.
