# SpecSight — Specification (v1)

**Status:** DRAFT — for review. Nothing is implemented yet.
**Date:** 2026-07-28

---

## 1. The one-sentence goal

> **SpecSight turns a Reqnroll test run into a single self-contained HTML report that shows every
> scenario as a step-wise Gherkin timeline, with screenshots hanging off UI steps and full
> HTTP request/response detail hanging off API steps — for one run or many.**

If a change does not serve that sentence, it is out of scope for v1. See §9 (Non-goals).

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
**evidence renderers** keyed by attachment media type. This single decision removes an
entire duplicated UI surface from the project, and it means a scenario that does both
(hits an API *and* asserts on the UI) renders correctly with no special-casing.

---

## 3. Architecture

```
   ┌──────────────────────────────────────────┐
   │  Your Reqnroll test project              │
   │                                          │
   │  SpecSight.Capture  (optional)             │
   │   ├─ AfterStep hook → screenshot         │
   │   └─ HttpClient DelegatingHandler        │
   │        → HTTP exchange attachment        │
   └────────────────┬─────────────────────────┘
                    │ Reqnroll's built-in `message` formatter
                    ▼
            run-2026-07-28.ndjson          ← Cucumber Messages (NDJSON)
                    │                         THE ONLY CONTRACT
                    ▼
   ┌──────────────────────────────────────────┐
   │  specsight report *.ndjson -o report.html  │
   │   ├─ SpecSight.Report  (parse + model)     │
   │   └─ evidence renderers by media type    │
   └────────────────┬─────────────────────────┘
                    ▼
              report.html   (self-contained, no CDN, no server)
```

### 3.1 The contract is Cucumber Messages, not our own format

The renderer reads **only** the NDJSON stream that Reqnroll's built-in `message` formatter
already produces. Consequences, all deliberate:

1. **`specsight` works on any Reqnroll project** with the message formatter enabled, even one
   that has never heard of `SpecSight.Capture`. You get the Gherkin timeline, statuses,
   durations, and errors for free.
2. **`SpecSight.Capture` is pure enrichment.** It adds attachments the renderer knows how to
   render richly. It is optional, and the two packages version independently.
3. **We never fork or patch Reqnroll.** No reliance on internal APIs; no reverse-engineering
   TRX or console output.
4. Free future option: the same renderer can consume Cucumber Messages from non-.NET
   ecosystems. *Not a v1 goal — just a door we are choosing not to nail shut.*

### 3.2 The evidence contract (media types)

`SpecSight.Capture` writes attachments; `SpecSight.Report` dispatches on them:

| Evidence | Detected by | Renderer | Produced by |
|---|---|---|---|
| Screenshot | media type `image/png` / `image/jpeg` | Inline thumbnail → lightbox | Capture, or any hook that attaches an image |
| HTTP exchange | media type `application/json` **and** top-level `"$specsight": "http/1"` field | HTTP exchange panel (§5.2) | Capture's `DelegatingHandler` |
| Log line | media type `text/x.cucumber.log+plain` or `text/plain` | Monospace line, collapsed if long | `IReqnrollOutputHelper.WriteLine` |
| anything else | — | Filename + size + download link | anyone |

Two notes on why the HTTP row is shaped that way:

- Reqnroll's `AddAttachment(filePath)` **infers the media type from the file extension** and
  offers no way to set a custom one, so a vendor media type like
  `application/vnd.specsight.http+json` is unreachable via the public API. The discriminator
  therefore lives **inside the payload**: a versioned `"$specsight": "http/1"` field. JSON
  attachments without it render as plain JSON — unknown things degrade, never break.
- Dispatching on the attachment itself — rather than on a SpecSight-specific envelope — is what
  keeps the renderer useful to people who only use half the toolkit.

### 3.3 Repo / package layout

```
SpecSight/
├─ README.md, LICENSE (MIT), .gitignore, Directory.Build.props
├─ docs/SPEC.md                  ← this file
├─ SpecSight.sln
├─ src/
│  ├─ SpecSight.Capture/           → NuGet: SpecSight.Capture   (Reqnroll runtime plugin)
│  ├─ SpecSight.Report/            → NuGet: SpecSight.Report    (library: parse + render)
│  └─ SpecSight.Cli/               → NuGet: SpecSight.Cli       (dotnet tool `specsight`)
├─ tests/
│  ├─ SpecSight.Report.Tests/      (golden-file tests over fixture .ndjson)
│  └─ fixtures/                  (recorded NDJSON: passing, failing, skipped, ui, api, multi-run)
└─ samples/
   └─ SampleTests/               (a real Reqnroll project — proves the pipeline end to end)
```

Three shipped packages, one sample, one test project. `SpecSight.Cli` is a thin argument-parsing
shell over `SpecSight.Report` so the library stays embeddable (e.g. an MSBuild task later).

---

## 4. Single-run report

### 4.1 Header / summary band
Run timestamp, total duration, counts by status (passed / failed / skipped / undefined /
ambiguous / pending), pass-rate ring, and the environment metadata Cucumber Messages'
`meta` envelope gives us (OS, CPU arch, .NET version, Reqnroll version, CI provider if detected).

### 4.2 Scenario list
Grouped by Feature, in feature-file order. Each row: status pill, scenario name, tags,
duration, and a one-line failure reason if failed. Scenario Outline example rows appear as
individual scenarios with their parameter values shown.

**Filters:** status, tag, feature, and a free-text search over scenario/step text.
**Default view:** failures expanded, passes collapsed. Nobody opens a report to admire the
passes; the failures should be on screen with zero clicks.

### 4.3 Step timeline (the core view)
For an expanded scenario, each step in order:

```
✓  Given  I am logged in as "admin"                          412 ms
✓  When   I request GET /api/orders                          128 ms   [1 HTTP]
✗  Then   the response status should be 200                    3 ms
   └─ Expected 200 but was 503
        at OrderSteps.ThenTheResponseStatusShouldBe(Int32) in OrderSteps.cs:line 42
⊘  And    the order list should contain 3 items                 —      (skipped)
```

- Keyword (`Given`/`When`/`Then`/`And`) rendered distinctly from the step text.
- Status glyph + colour; duration right-aligned.
- Failed step expands to message + stack trace, with our own source shown above framework frames.
- **Evidence badges** (`[1 HTTP]`, `[2 shots]`) on the step row; clicking expands the evidence
  panel inline. Evidence is lazy-rendered so a 500-step report stays responsive.
- Data tables and doc strings attached to a step render as a proper table / preformatted block.

### 4.4 Skipped steps — verified behaviour
Reqnroll's `AfterStep`/`BeforeStep` hooks do **not** fire for steps skipped after an earlier
failure — reqnroll/Reqnroll#892, closed *by design*, will not change. However, the **message
stream itself is complete**: Reqnroll emits `testStepFinished` with status `SKIPPED` for those
steps regardless of hooks. Two rules follow:

1. The renderer's primary source for skip status is the stream. As a *defensive fallback only*
   (e.g. a crashed run producing a truncated stream), steps after the first failure with no
   `testStepFinished` are rendered as skipped.
2. `SpecSight.Capture` must never assume its step hooks fire once per step — evidence capture is
   best-effort per executed step, and the report must not depend on hook coverage.

---

## 5. Evidence renderers

### 5.1 Screenshots
Thumbnail strip on the step row; click opens a lightbox with prev/next across the scenario's
shots and a filename/timestamp caption. Baseline/diff comparison is **not** in v1.

### 5.2 HTTP exchange panel
The panel that makes this tool worth building for API suites:

```
POST  https://api.example.com/v2/orders                    503  ·  128 ms
├─ Request
│    Headers      Content-Type: application/json
│                 Authorization: ●●● redacted
│    Body         { "sku": "ABC-123", "qty": 2 }          [pretty | raw | copy]
└─ Response
     Headers      x-request-id: 7f3a...
     Body         { "error": "upstream_unavailable" }     [pretty | raw | copy]
```

- Status code colour-coded by class (2xx/3xx/4xx/5xx); latency shown.
- JSON bodies pretty-printed and syntax-highlighted, with a raw toggle and copy button.
- Multiple exchanges on one step are listed in order.
- Redaction and truncation markers are always visible (§8) — never silently hidden.

---

## 6. Multi-run report

Passing more than one NDJSON file produces an aggregate report. Multi-run is not a bolt-on:
one run is simply the degenerate case of N runs, so the model is built for N from day one.

### 6.1 Scenario identity across runs
Cucumber `pickle` ids are **not** stable between runs, so identity is derived:

```
identity = feature file URI  +  scenario name  +  ordered example-row parameter values
```

Renaming a scenario therefore reads as "old one disappeared, new one appeared." Documented
limitation; a `--identity-map` file is a v2 escape hatch, not v1.

### 6.2 Views
- **Trend band** — pass rate and duration per run, oldest → newest.
- **Scenario matrix** — rows = scenarios, columns = runs, cells = status. The single most
  useful artifact for spotting a scenario that broke three runs ago.
- **Flaky list** — scenarios with more than one distinct outcome across the run set, ranked by
  flip count. *Flaky here means "inconsistent across the runs you supplied"* — an honest,
  narrow definition, not a statistical claim.
- **Drill-down** — any cell opens that run's full step timeline for that scenario.

Runs are ordered by their `meta` timestamp, not by filename or CLI argument order.

---

## 7. CLI surface

```bash
specsight report <paths...> [options]
```

`<paths>` accepts files, globs, or directories (recursed for `*.ndjson`).

| Option | Default | Purpose |
|---|---|---|
| `-o, --output <path>` | `specsight-report.html` | Output file |
| `--assets <inline\|external>` | `inline` | `external` writes images to `<output>_files/` |
| `--title <text>` | derived | Report title |
| `--max-inline-bytes <n>` | `50MB` | Abort with guidance rather than emit an unusable file |
| `--open` | off | Open in the default browser when done |
| `--fail-on <none\|any-failure>` | `none` | Exit code 1 — lets CI gate on the report step |

Exit codes: `0` success · `1` `--fail-on` triggered · `2` bad input (no files, malformed NDJSON).

---

## 8. Security and size rules

These are hard requirements, not polish.

1. **Header redaction is on by default.** `Authorization`, `Proxy-Authorization`, `Cookie`,
   `Set-Cookie`, `X-API-Key`, and any header matching `*token*`, `*secret*`, `*password*`
   (case-insensitive) are replaced with `●●● redacted`. Configurable via allow/deny lists.
   **The redaction happens at capture time** — a secret must never reach the NDJSON file, because
   that file gets archived as a CI artifact.
2. **Body capture is size-capped** (default 64 KB per body). Truncation is shown explicitly:
   `… truncated, 1.2 MB total`.
3. **A body redaction hook** (`Func<HttpContent, string>`) lets teams scrub PII from bodies.
4. **Screenshots default to on-failure-only.** Per-step capture is opt-in, because a 200-scenario
   suite with per-step screenshots produces a report nobody can open.
5. **Screenshots are captured as size-bounded JPEG.** Reqnroll inlines every attachment into
   the NDJSON as base64 with no size cap and no external-file option (verified — see
   `reqnroll-api-notes.md` §3), so every attachment byte lands in a file that CI archives.
   Capture re-encodes screenshots to JPEG at a configurable quality (default ~40) before
   attaching. Externalizing assets is the renderer's job (`--assets external`), not Reqnroll's.
6. **The HTML makes zero network requests.** No CDN, no fonts, no telemetry. It must open from
   a `file://` URL on a locked-down machine and render identically.
7. **All run data is injected as a JSON data island and escaped**, so a scenario named
   `</script><img onerror=...>` cannot execute in the report.

---

## 9. Non-goals for v1

Written down so they can be *added later on purpose* rather than drifting in during the build:

- No user-facing templating/theming engine. (Light/dark auto-switch, that's it.)
- No history server, database, or trend storage. Static files in, static file out.
- No GitHub/Azure DevOps check-run or PR-comment integration.
- No video capture, no Playwright trace embedding.
- No live/streaming report during a run.
- No visual regression / baseline image diffing.
- No custom Reqnroll formatter plugin — we consume the built-in `message` formatter's output.
  This non-goal is now verified as correct, not just cautious: the `ICucumberMessageFormatter`
  extension point exists but Reqnroll's docs explicitly warn it may change significantly even
  in minor versions. File-based consumption is the stable seam.

---

## 10. Milestones

| # | Deliverable | Done when |
|---|---|---|
| **M0** | Scaffold + sample Reqnroll project | `dotnet test` on `samples/` emits a valid `.ndjson`; that file is committed as fixture #1 |
| **M1** | NDJSON → `RunModel` | Parser handles pass/fail/skip/outline/datatable/docstring/attachment; golden tests green |
| **M2** | Single-run HTML | Summary + scenario list + step timeline + errors + filters. **First genuinely useful release.** |
| **M3** | Evidence + Capture package | Screenshot and HTTP panels render; `SpecSight.Capture` auto-loads on package reference; redaction proven by test |
| **M4** | Multi-run | Trend, matrix, flaky list, drill-down |
| **M5** | Ship | README with real screenshots, CI build, NuGet publish, semver 1.0.0 |

M2 is the honest MVP — it is worth using even if M3–M4 never happen. That is the property we
want out of a milestone plan.

---

## 11. Technology decisions

| Decision | Choice | Rationale |
|---|---|---|
| Runtime | CLI/Report: .NET 10. Capture: **netstandard2.0** | Capture must load inside any consumer's test process; netstandard2.0 is the widest net (and what Reqnroll's plugin docs prescribe) |
| Capture assembly name | `SpecSight.Capture.ReqnrollPlugin` (package id stays `SpecSight.Capture`) | Reqnroll only auto-loads plugin assemblies whose file name ends in `.ReqnrollPlugin.dll` — verified, see api notes §5 |
| Reqnroll support range | v3.0+ (v3.3+ for output-path `{timestamp}` etc.) | Formatters shipped in 3.0; substitution variables in 3.3 |
| Distribution | `dotnet tool install -g SpecSight.Cli` | Zero-friction for .NET teams |
| Report UI | **Hand-written HTML + CSS + vanilla JS**, embedded as a resource, hydrated from a JSON data island | Keeps the repo a pure .NET build with no Node toolchain in CI, and guarantees the single-file/no-CDN property. The interactions here are expand, filter, lightbox, tab — a framework would not earn its complexity. Escape hatch: if the aggregate views outgrow this, swap in Preact behind the same data-island contract. |
| JSON | `System.Text.Json` with source-generated contexts | No extra dependency; fast on large streams |
| Tests | xUnit + golden-file HTML snapshots | Report regressions are exactly what snapshot tests are good at |
| Parsing | Stream NDJSON line-by-line | A long run's message file can be hundreds of MB; never `ReadAllText` |

---

## 12. Open questions for review

1. ~~**Name**~~ — RESOLVED: **SpecSight** (packages `SpecSight.Capture` / `SpecSight.Report` /
   `SpecSight.Cli`, CLI `specsight`). Package ids verified free on nuget.org 2026-07-28.
   Discoverability for "reqnroll report" searches comes from package tags/description
   (`reqnroll`, `html-report`, `gherkin`, `living-documentation`, `test-report`), which
   nuget.org indexes alongside the id.
2. **Is M2 (no evidence panels) genuinely useful to you**, or is the HTTP panel the reason you
   want this at all? If the latter, M3 moves ahead of M2's filter/search polish.
3. **How do your API tests issue HTTP calls today?** The `DelegatingHandler` approach only sees
   traffic through an `HttpClient` it is attached to. If your suites use RestSharp, Refit, or a
   hand-rolled wrapper, the capture surface needs a different (still small) integration.
4. **Public NuGet or internal feed?** Changes the licence, CI, and README posture.
5. **Repo location** — currently a sibling of `PlaywrightRecorderTool`, standalone git repo, no
   remote yet.

---

## 13. Technical appendix — verified Reqnroll API surface

> Filled in from a live docs check; see `docs/reqnroll-api-notes.md`.
