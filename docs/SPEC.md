# SpecReport — Specification (v1.1)

**Status:** REVIEWED DRAFT — v1.0 was put through a live Reqnroll API verification
(`reqnroll-api-notes.md`) and an adversarial design review; this revision incorporates both.
Nothing is implemented yet.
**Date:** 2026-07-28

---

## 1. The one-sentence goal

> **SpecReport turns a Reqnroll test run into a single self-contained HTML report that shows
> every scenario as a step-wise Gherkin timeline, with screenshots hanging off UI steps and
> full HTTP request/response detail hanging off API steps — for one run or many.**

If a change does not serve that sentence, it is out of scope for v1. See §9 (Non-goals).

### 1.1 The baseline we must beat

Reqnroll ships a built-in `html` formatter (the Cucumber HTML formatter) that already renders
a step timeline with statuses, durations, errors, and basic filtering — for free, with two
lines of `reqnroll.json`. **A report that only re-does that is not worth shipping.** SpecReport
earns its existence with exactly two differentiated features, and the milestone plan (§10)
puts one of them in the first demo:

1. **The HTTP exchange panel** — request/response/header/latency detail on the step that made
   the call.
2. **Multi-run aggregation** — trend, scenario×run matrix, flaky detection. The built-in
   formatter is strictly single-run.

Every scope decision downstream should be tested against this framing.

---

## 2. The key design insight

The obvious framing is *"frontend reports look like X, backend reports look like Y — build two
report types."* **We are explicitly not doing that.**

Both frontend and backend BDD tests are Gherkin. Both want the same skeleton: Feature →
Scenario → ordered steps, each with a status, a duration, and an error if it failed. What
differs is only the **evidence attached to each step**:

| Test kind | Evidence on a step |
|---|---|
| Frontend | Screenshot(s), console/log text |
| Backend | HTTP exchange: method, URL, request headers/body, status, response headers/body, latency |
| Either | Plain-text log lines, arbitrary file attachments |

So there is **one report layout** — a Gherkin step timeline — and a set of pluggable
**evidence renderers** dispatched per attachment. This single decision removes an entire
duplicated UI surface from the project, and it means a scenario that does both (hits an API
*and* asserts on the UI) renders correctly with no special-casing.

---

## 3. Architecture

```
   ┌──────────────────────────────────────────┐
   │  Your Reqnroll test project              │
   │                                          │
   │  SpecReport.Capture  (optional)          │
   │   ├─ AfterStep hook → screenshot         │
   │   └─ HttpClient DelegatingHandler        │
   │        → buffered, drained per step      │
   └────────────────┬─────────────────────────┘
                    │ Reqnroll's built-in `message` formatter
                    ▼
            run-2026-07-28.ndjson          ← Cucumber Messages (NDJSON)
                    │                         THE ONLY CONTRACT
                    ▼
   ┌──────────────────────────────────────────┐
   │ specreport <paths> -o report.html        │
   │   ├─ SpecReport.Core  (parse + model)    │
   │   └─ evidence renderers                  │
   └────────────────┬─────────────────────────┘
                    ▼
              report.html   (self-contained, no CDN, no server)
```

### 3.1 The contract is Cucumber Messages, not our own format

The renderer reads **only** the NDJSON stream that Reqnroll's built-in `message` formatter
already produces (verified: formatters ship in core Reqnroll v3.0+, no extra package).
Consequences, all deliberate:

1. **`specreport` works on any Reqnroll project** with the message formatter enabled, even one
   that has never heard of `SpecReport.Capture`. You get the Gherkin timeline, statuses,
   durations, and errors for free.
2. **`SpecReport.Capture` is pure enrichment.** It adds attachments the renderer knows how to
   render richly. It is optional, and the two packages version independently.
3. **We never fork or patch Reqnroll.** No reliance on internal APIs; no reverse-engineering
   TRX or console output. (The `ICucumberMessageFormatter` extension point exists but is
   documented as unstable across minor versions — file consumption is the stable seam.)
4. Free future option: the same renderer can consume Cucumber Messages from non-.NET
   ecosystems. *Not a v1 goal — just a door we are choosing not to nail shut.*

**The one sanctioned escape hatch.** Real report data exists that Cucumber Messages cannot
carry: environment name, browser/viewport, build number, shard label — the "was that failure
on UAT or Prod?" answers a trend view demands. Rather than let these arrive later as sidecar
files that quietly break the one-contract rule, v1 defines a **run-meta attachment**: emitted
at most once per run by `SpecReport.Capture` (or by the user's own hook), a JSON attachment
with `"$specreport": "runmeta/1"` carrying a free-form string dictionary. It travels inside
the NDJSON, degrades to nothing when absent, and keeps "one contract" literally true. A
`--run-meta` CLI sidecar option is **explicitly rejected**.

### 3.2 The evidence contract

Reqnroll's `AddAttachment(filePath)` infers the media type from the file extension and offers
no way to set a custom one (verified), so vendor media types are unreachable. The
discriminator therefore lives **inside the payload**: a versioned `"$specreport"` field.
JSON attachments without it render as plain JSON — unknown things degrade, never break.

| Evidence | Detected by | Renderer |
|---|---|---|
| Screenshot | media type `image/png` / `image/jpeg` | Inline thumbnail → lightbox |
| HTTP exchange | JSON with `"$specreport": "http/1"` | HTTP exchange panel (§5.2) |
| Run metadata | JSON with `"$specreport": "runmeta/1"` | Header band key/values (§4.1) |
| Log line | media type `text/x.cucumber.log+plain` or `text/plain` | Monospace line, collapsed if long |
| anything else | — | Filename + size + download link |

The `http/1` payload carries its own `startedAt` timestamp and sequence number, because the
Cucumber `attachment` envelope has no timestamp — without this, multiple exchanges on one
step could never show intra-step timing.

### 3.3 Repo / package layout

```
SpecReport/
├─ README.md, LICENSE (MIT), .gitignore, Directory.Build.props
├─ docs/SPEC.md                  ← this file
├─ docs/reqnroll-api-notes.md    ← verified API surface (2026-07-28)
├─ SpecReport.sln
├─ src/
│  ├─ SpecReport.Capture/        → NuGet: SpecReport.Capture  (Reqnroll runtime plugin, netstandard2.0)
│  ├─ SpecReport.Core/           → NuGet: SpecReport.Core     (library: parse + model + render)
│  └─ SpecReport.Cli/            → NuGet: SpecReport.Cli      (dotnet tool `specreport`)
├─ tests/
│  ├─ SpecReport.Core.Tests/     (RunModel snapshot tests over fixture .ndjson)
│  ├─ report-ui-tests/           (small Playwright suite over the emitted HTML — separate CI job)
│  └─ fixtures/                  (recorded NDJSON — see §10 M0/M2 for the required set)
└─ samples/
   └─ SampleTests/               (a real Reqnroll project — proves the pipeline end to end)
```

`SpecReport.Cli` is a thin argument-parsing shell over `SpecReport.Core` so the library stays
embeddable (e.g. an MSBuild task later).

### 3.4 HTTP capture design (the part that must not be naive)

A `DelegatingHandler` cannot know the "current step" directly: it lives in the SUT-client's
DI world, not Reqnroll's, and the tempting fixes are traps — assigning `AsyncLocal<T>.Value`
inside an async hook mutates a copy-on-write ExecutionContext and is silently lost when the
hook returns; `[ThreadStatic]` breaks the moment an async step resumes on another pool thread.
Under parallel execution a naive design **attributes exchanges to the wrong step**, which is
worse than no tool at all. So:

1. **Capture and attribution are decoupled.** The handler only appends completed exchanges to
   a scenario-scoped, thread-safe buffer. It never asks "what step is this?"
2. **Attribution happens at step boundaries.** Capture's `[AfterStep]` hook (which *does* have
   `ScenarioContext`) drains everything buffered since the previous drain and attaches it —
   Reqnroll stamps the current `testStepId` automatically (verified). In-step ordering is by
   capture sequence.
3. **The ambient handle is scenario-scoped and holder-based.** The buffer is registered in
   Reqnroll's scenario DI container; the handler reaches it through an `AsyncLocal` holding a
   **mutable holder object** seeded once per scenario — the holder's contents change, the
   `AsyncLocal.Value` is never reassigned mid-flow.
4. **Nothing is dropped, nothing is misattributed.** Exchanges left in the buffer at scenario
   end (made in `BeforeScenario`/`AfterScenario` hooks, background polling) drain into a
   labelled **"outside steps"** bucket rendered at scenario level.
5. **Body capture is explicit-wiring = consent.** The handler buffers request/response bodies
   (size-capped, §8) — acceptable because the team explicitly attached it. Callers using
   `HttpCompletionOption.ResponseHeadersRead`/streaming get headers + a "body not captured
   (streaming)" marker rather than broken streaming semantics.
6. Process-wide zero-wiring capture via `DiagnosticListener`/`ActivitySource` (sees RestSharp,
   Refit, SDK clients with no setup) is a **v2 candidate**, noted in §9 — v1 ships the
   explicit handler plus a documented "wrap your client factory" recipe.

**This design is milestone M1's spike subject** — proven under 4-way parallel async execution
before anything is built on it.

### 3.5 Framework adaptability — a hard rule, not a hope

SpecReport must work with **any** stack a Reqnroll project can be built on. That is trivially
true for the report side and requires one deliberate rule on the capture side:

**Report side (`SpecReport.Core` / `SpecReport.Cli`): agnostic by construction.** They consume
only the NDJSON file — they never load into, reference, or care about the consumer's process.
Test runner (MSTest / NUnit / xUnit), UI driver, HTTP library, target framework: all
irrelevant.

**Capture side (`SpecReport.Capture`): zero framework references, adapters via delegates.**
The rule: `SpecReport.Capture` references **only** `Reqnroll` and the BCL — never Playwright,
Selenium, RestSharp, Refit, or any other driver/client library. Coupling is inverted at the
two points where it could creep in:

1. **Screenshots.** Capturing one needs the consumer's driver handle (`IPage`, `IWebDriver`,
   …), so Capture defines a provider the consumer registers in a `[BeforeScenario]` hook:

   ```csharp
   // Playwright consumer
   capture.UseScreenshotProvider(ct => _page.ScreenshotAsync(...));
   // Selenium consumer
   capture.UseScreenshotProvider(ct => Task.FromResult(
       ((ITakesScreenshot)_driver).GetScreenshot().AsByteArray));
   ```

   One delegate, any driver, no SpecReport dependency on either. API-only suites simply never
   register one. Optional convenience adapter packages (`SpecReport.Capture.Playwright`, …)
   are a **post-v1** nicety — each would be ~50 lines wrapping the same delegate — and must
   never become a requirement.
2. **HTTP.** Virtually every modern .NET HTTP library — Refit, RestSharp (v107+), Flurl,
   `IHttpClientFactory` typed clients — is built on `HttpClient`/`HttpMessageHandler`, so the
   single `DelegatingHandler` of §3.4 covers all of them; only the one-line wiring recipe
   differs per library, and the README ships a recipe per stack. Stacks *not* built on
   `HttpClient` (legacy `HttpWebRequest`, gRPC, raw sockets) are outside v1's handler and are
   exactly what the v2 `DiagnosticListener` candidate (§9) exists for.

Enforced mechanically: `SpecReport.Capture.csproj` gets a CI check that its package
dependencies are `Reqnroll` and nothing else.

### 4.1 Header / summary band
Run timestamp (from `testRunStarted` — the `meta` envelope has no timestamp), total duration,
counts by status (passed / failed / skipped / undefined / ambiguous / pending), pass-rate
ring, environment info from `meta` (OS, CPU, runtime, CI provider when populated), and any
`runmeta/1` key/values (§3.1). If the stream was truncated or lines were skipped, a visible
**"run did not complete — N lines unreadable"** banner appears here (§7.1).

### 4.2 Scenario list
Grouped **Feature → Rule → Scenario** (Gherkin `Rule:` gets a grouping slot — scenarios under
different Rules must not merge), in feature-file order (derived from the `gherkinDocument`
AST — the execution stream is in *execution* order under parallelism, so the sort key comes
from the AST, not arrival order). Each row: status pill, scenario name, tags, duration,
one-line failure reason. Scenario Outline example rows appear as individual entries with
their parameter values.

**Filters:** status, tag, feature, free-text search over scenario/step text.
**Default view:** failures expanded, passes collapsed.

### 4.3 Step timeline (the core view)

```
▸  Before hook                                              203 ms   (collapsed when passed)
✓  Given  I am logged in as "admin"                          412 ms
✓  When   I request GET /api/orders                          128 ms   [1 HTTP]
✗  Then   the response status should be 200                    3 ms
   └─ Expected 200 but was 503
        at OrderSteps.ThenTheResponseStatusShouldBe(Int32) in OrderSteps.cs:line 42
⊘  And    the order list should contain 3 items                 —      (skipped)
```

- **Hook rows are first-class.** `Before`/`After` hooks are testSteps with a `hookId` — not
  pickle steps — and a failing `BeforeScenario` (DB seed, login, browser launch: the most
  common CI failure) would otherwise render as "all steps skipped, no error anywhere."
  Hook rows are visually de-emphasised, collapsed when passed, **always expanded when
  failed**, with the exception shown. This also gives hook-scoped evidence a home.
- Gherkin keyword (`Given`/`When`/`Then`/`And`/`But`) rendered distinctly. Keywords come from
  the `gherkinDocument` AST joined via `astNodeIds` — the pickle carries text but not
  keywords. **Styling keys off Cucumber's `keywordType`** (Context/Action/Outcome/
  Conjunction), display shows the raw keyword — so `Dado`/`Angenommen`/`假如` (localized
  Gherkin) work with zero special-casing.
- **Background steps** are labelled as such (the pickle inlines them; without the AST join
  they'd read as anonymous leading steps in every scenario).
- Status glyph **and text** (never colour/glyph alone — §8.8), duration right-aligned.
- Failed steps expand to message + stack trace. "Our" frames — those whose declaring assembly
  matches the step-definition assemblies from the `stepDefinition` envelopes — are shown above
  framework frames; overridable via `--user-namespaces`.
- **Evidence badges** (`[1 HTTP]`, `[2 shots]`) expand inline; evidence is lazy-rendered.
- Data tables / doc strings render as a table / preformatted block.
- Known limitation (documented, not solved): nested/called steps produce no pickle steps —
  their time, failures, and evidence roll up into the outer step.

### 4.4 Skipped steps — precedence rule
The message stream is authoritative: Reqnroll emits `testStepFinished` with `SKIPPED` for
steps after a failure even though step hooks don't fire (#892, closed by-design). Inference
— "steps after the first failure with no `testStepFinished`" — is a **fallback only**, applies
only where the stream is silent, never overrides a reported status, and inferred skips are
visually marked (`(inferred)`) so they can't be mistaken for reported data.

### 4.5 Retries
Cucumber Messages models retries: `testCaseStarted.attempt`, `testCaseFinished.willBeRetried`.
Untreated, a retried scenario triple-counts in the summary and poisons the flaky list. Rules:

- Group by `testCaseId`; the **last attempt is the scenario's outcome**; all counts use final
  outcomes only.
- Earlier attempts render as expandable "attempt 1 of 3" sub-entries with their own timelines.
- Failed-then-passed within one run is surfaced as **flaky-within-run** — a stronger signal
  than cross-run flipping, and nearly free once attempts are modelled.
- `testCaseStarted.workerId` is display metadata (which worker/shard), never an ordering key.

---

## 5. Evidence renderers

### 5.1 Screenshots
Thumbnail strip on the step row; lightbox with prev/next across the scenario's shots,
filename/timestamp caption, full keyboard operation (§8.8). No baseline/diff comparison in v1.

### 5.2 HTTP exchange panel

```
POST  https://api.example.com/v2/orders?apiKey=●●●          503  ·  128 ms
├─ Request
│    Headers      Content-Type: application/json
│                 Authorization: ●●● redacted
│    Body         { "sku": "ABC-123", "qty": 2 }          [pretty | raw | copy]
└─ Response
     Headers      x-request-id: 7f3a...
     Body         { "error": "upstream_unavailable" }     [pretty | raw | copy]
```

- Status colour-coded by class; latency shown; **query-string secrets redacted** (§8.1).
- JSON bodies pretty-printed with raw toggle and copy button.
- Multiple exchanges on a step are ordered by the payload's own sequence/timestamp (§3.2).
- Redaction and truncation markers always visible — never silently hidden.
- Assertion expected-vs-actual **diff views are out of scope for v1** — that requires
  regex-parsing FluentAssertions/NUnit/Shouldly message strings (fragile, per-library). v1
  shows the raw failure message honestly; a diff heuristic is a v2 candidate.

---

## 6. Multi-run report

### 6.1 What is "a run"? (sharding)
CI regularly splits one logical run across N agents or N test projects → N NDJSON files.
Treating files as runs would render four bogus trend points and a matrix that's 75% holes.
So the unit is explicit:

- **Convention:** one directory per logical run; all `.ndjson` files inside a directory merge
  into one run (scenarios unioned, end-time = max).
- `specreport runs/2026-07-28/ runs/2026-07-29/` → two runs.
- A flat list of files = one run per file (the simple case still works).
- Runs are ordered by `testRunStarted.timestamp` (UTC), not filename or argument order.

### 6.2 Scenario identity across runs
Cucumber `pickle` ids are not stable between runs, so:

```
identity = feature URI + rule name + scenario name + example-row values + example-row index
```

The row index breaks ties between *identical* example rows (common in real suites; without it
the matrix silently merges them). Renaming a scenario reads as removed+added — documented
limitation; an `--identity-map` is a v2 escape hatch.

### 6.3 Views
- **Trend band** — pass rate and duration per run, oldest → newest.
- **Scenario matrix** — rows = scenarios, columns = runs, cells = status **with a third state:
  `not run`** (present in other runs, absent here — sharding gaps, tag filters). `not run` is
  visually distinct from skipped and excluded from flaky detection.
- **Flaky list** — scenarios with >1 distinct *executed* outcome across the run set, plus any
  flaky-within-run (§4.5), ranked by flip count. *Flaky means "inconsistent across the runs
  you supplied"* — an honest, narrow definition.
- **Drill-down** — any cell opens that run's full step timeline for that scenario.

---

## 7. CLI surface

```bash
specreport <paths...> [options]
```

`<paths>`: files, globs, or directories (a directory = one merged run, §6.1).

| Option | Default | Purpose |
|---|---|---|
| `-o, --output <path>` | `specreport.html` | Output file |
| `--assets <inline\|external>` | `inline` | `external` writes binaries to `<output>_files/` |
| `--title <text>` | derived | Report title |
| `--max-inline-bytes <n>` | `50MB` | Threshold for auto-degrade (§7.1), not an abort |
| `--user-namespaces <list>` | from `stepDefinition` envelopes | Stack-frame "ours" override |
| `--open` | off | Open in default browser when done |

Exit codes: `0` report produced (possibly with warnings) · `2` no input files / zero valid
envelopes. (`--fail-on` was cut: `dotnet test` already failed the build; a meaningful
`--fail-on new-failure` needs multi-run context and is deferred to post-M5.)

### 7.1 Degraded input is the most important input
The run that most needs a report is the one where the test host was killed by a CI timeout —
leaving a truncated final line, no `testRunFinished`, and scenarios with a `testCaseStarted`
but no finish. The parser is therefore **best-effort by design**:

- Unparseable lines are skipped and counted; a truncated last line is tolerated.
- Scenarios with no `testCaseFinished` get status `unknown`, rendered distinctly.
- All degradation is surfaced in the header banner (§4.1) — visible, never silent.
- **Unknown envelope types and unknown JSON properties are ignored** (forward compatibility
  with future Reqnroll releases; `meta.protocolVersion` mismatch warns, never errors).
- Oversized output auto-degrades instead of aborting: fall back to `--assets external`, or
  drop largest attachments with a visible "N attachments omitted" marker. Exit 0 + warning.

---

## 8. Security, size, and accessibility rules

Hard requirements, not polish.

1. **Redaction happens at capture time** — the NDJSON gets archived as a CI artifact, so
   secrets must never reach it. Defaults, all case-insensitive pattern `*token*`, `*secret*`,
   `*password*`, `*apikey*`, `*authorization*`, `*cookie*`, `*session*`:
   - **Headers** matching the list (plus `Authorization`, `Proxy-Authorization`, `Cookie`,
     `Set-Cookie`, `X-API-Key`) → `●●● redacted`.
   - **Query-string parameters** matching the list (SAS tokens, `?api_key=`, reset tokens).
   - **JSON body keys** matching the list — deep-scanned, values replaced.
   Configurable allow/deny lists; a `Func<...>` hook for custom body scrubbing (PII).
2. **What is NOT redacted by default, stated honestly:** free-text (non-JSON) bodies,
   response bodies that *are* the secret (a login returning a raw JWT as `text/plain`), and
   anything visible **inside screenshots**. The README says this plainly — an overclaimed
   security guarantee is worse than none.
3. **Body capture is size-capped** (default 64 KB per body) with explicit truncation markers:
   `… truncated, 1.2 MB total`.
4. **Screenshots default to on-failure-only** and are **re-encoded to bounded JPEG** at
   capture time (configurable quality ≈40, max dimension cap) — Reqnroll inlines every
   attachment byte into the NDJSON as base64 with no size cap and no external option
   (verified), so unbounded PNGs would produce 100+ MB artifacts.
5. **The HTML makes zero network requests.** No CDN, no fonts, no telemetry. Must render
   identically from `file://` on a locked-down machine.
6. **All run data is injected as an escaped JSON data island** — a scenario named
   `</script><img onerror=...>` cannot execute in the report.
7. **Scale budget:** 5,000 scenarios / 50,000 steps / 30 runs must open in < 3 s and stay
   interactive on a mid-range laptop. A synthetic fixture at this size exists from M2
   onward — before any UI is written. The step list and matrix are virtualized if (and only
   if) the budget demands it — measured, not assumed. **Memory:** attachment bodies are
   spooled to a temp directory during parse and streamed into the output at write time —
   peak memory is O(largest attachment), not O(all attachments).
8. **Accessibility:** status conveyed by glyph *and text*, never colour alone; full keyboard
   operability (expand/collapse, filters, lightbox with Escape-to-close); visible focus
   rings; WCAG AA contrast in both themes. Times display in the viewer's local timezone,
   labelled with UTC offset; ordering always uses the UTC timestamps.

---

## 9. Non-goals for v1

Written down so they can be *added later on purpose* rather than drifting in during the build:

- No user-facing templating/theming engine. (Light/dark auto-switch, that's it.)
- No history server, database, or trend storage. Static files in, static file out.
- No GitHub/Azure DevOps check-run or PR-comment integration.
- No video capture, no Playwright trace embedding.
- No live/streaming report during a run.
- No visual regression / baseline image diffing.
- No custom Reqnroll formatter plugin — the `ICucumberMessageFormatter` API is explicitly
  unstable across minor versions; the built-in `message` formatter's file output is the seam.
- No assertion expected/actual diff parsing (v1 shows raw failure messages — §5.2).
- No zero-wiring process-wide HTTP capture via `DiagnosticListener` (v2 candidate — §3.4).
- No `--fail-on` CI gating (revisit as `new-failure` semantics after M5).

---

## 10. Milestones

Reordered after review: the spike moved ahead of everything (highest-risk unknown first), and
**evidence panels moved ahead of filter/search polish** — because §1.1's baseline means a
plain timeline report is not differentiated, and the HTTP panel is.

| # | Deliverable | Done when |
|---|---|---|
| **M0** | Scaffold + sample Reqnroll project | `dotnet test` on `samples/` emits valid `.ndjson`; committed as fixture #1. Sample includes: pass, fail, outline, Background, Rule, data table, docstring, failing BeforeScenario, failing AfterScenario, PNG + JSON attachments, one localized-Gherkin feature |
| **M1** | **Spike: HTTP attribution under parallelism** | Prototype buffer-and-drain capture proven correct under 4-way parallel async execution (deliberate interleaving test); findings written back into §3.4. Throwaway code allowed |
| **M2** | NDJSON → `RunModel` | Full parse incl. **gherkinDocument AST join** (keywords, Background, Rule, outline example values), hook steps, retries/attempts, best-effort degraded input (§7.1); RunModel snapshot tests green over the full fixture set + a truncated fixture + a synthetic 5k-scenario fixture |
| **M3** | Single-run HTML **with evidence panels** | Timeline + hook rows + errors + screenshot lightbox + HTTP panel (from hand-authored `http/1` fixtures). **The first demo — leads with the differentiator** |
| **M4** | `SpecReport.Capture` productized | Auto-loads on package reference (`.ReqnrollPlugin.dll` naming); screenshot + HTTP capture per §3.4; redaction proven by tests (secrets planted in fixture traffic must not appear in NDJSON) |
| **M5** | Multi-run | Run grouping (§6.1), trend, matrix with `not run` state, flaky list, drill-down |
| **M6** | Polish + ship | Filters/search, accessibility pass (§8.8), scale budget verified against the 5k fixture, README with real screenshots, CI, NuGet publish 1.0.0 |

After M3 the tool is demo-ably differentiated; after M4 it is genuinely useful end-to-end.

---

## 11. Technology decisions

| Decision | Choice | Rationale |
|---|---|---|
| Runtime | CLI/Core: .NET 10. Capture: **netstandard2.0** | Capture must load inside any consumer's test process; netstandard2.0 is the widest net (and what Reqnroll's plugin docs prescribe) |
| Capture assembly name | `SpecReport.Capture.ReqnrollPlugin` (package id stays `SpecReport.Capture`) | Reqnroll only auto-loads plugin assemblies whose file name ends in `.ReqnrollPlugin.dll` — verified |
| Reqnroll support range | v3.0+ (v3.3+ for output-path `{timestamp}` etc.) | Formatters shipped in 3.0; substitution variables in 3.3 |
| Distribution | `dotnet tool install -g SpecReport.Cli` | Zero-friction for .NET teams |
| Report UI | Hand-written HTML/CSS/vanilla JS, embedded resource, hydrated from a JSON data island | Keeps the **shipping** build pure .NET and guarantees the single-file/no-CDN property. Revisited at M3 against the scale budget (§8.7) — if virtualization pushes complexity past what vanilla comfortably carries, swap in Preact behind the same data-island contract *at M3*, not after |
| Report JS testing | Small Playwright suite over the emitted HTML, in `tests/report-ui-tests/`, **separate CI job with a Node dev-dependency** | The riskiest code in the project must not be the only untested code. The package build remains Node-free; only the test job needs Node |
| Parser testing | **Snapshot the `RunModel` JSON data island**, not the HTML | The data island is the actual contract and is stable under restyling. 2–3 full-HTML goldens kept purely as smoke tests |
| JSON | `System.Text.Json`, source-generated, unknown members ignored | No extra dependency; forward-compatible per §7.1 |
| Parsing | Stream NDJSON line-by-line; spool attachment bodies to temp files | Bounded memory even when a single line is a 5 MB base64 image (§8.7) |

---

## 12. Open questions

1. ~~Name~~ — RESOLVED: **SpecReport** (packages `SpecReport.Capture` / `SpecReport.Core` /
   `SpecReport.Cli`, CLI `specreport`) — chosen so the name itself says "report", per review
   of alternatives. Ids verified free on nuget.org 2026-07-28. NuGet tags reinforce
   discoverability: `reqnroll`, `html-report`, `gherkin`, `living-documentation`,
   `test-report`, `bdd`.
2. ~~Is a plain timeline report useful without evidence?~~ — RESOLVED by review: no — the
   built-in `html` formatter already covers it (§1.1). Milestones reordered so evidence
   ships first.
3. **OPEN — Which HTTP stack recipe ships first in the README?** Any `HttpClient`-based
   library works with the §3.4 handler (see §3.5); the question is only which wiring recipe
   (plain `HttpClient` / `IHttpClientFactory` / RestSharp / Refit / Flurl) is written and
   tested first — decided by whichever the author's own suites use.
4. **OPEN — Public NuGet or internal feed?** Changes licence posture, CI, and README.

---

## 13. Technical appendix

Verified Reqnroll API surface (formatters config, emitted envelopes, attachment semantics,
hook behaviour, plugin packaging): see [`reqnroll-api-notes.md`](reqnroll-api-notes.md),
checked against Reqnroll v3.3.4 docs + source on 2026-07-28.
