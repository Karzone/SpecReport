<div align="center">

<img src="assets/icon.png" alt="" width="96" height="96">

# SpecReport

**HTML reports for [Reqnroll](https://reqnroll.net) test runs.**
Gherkin step timelines with screenshots for UI tests, full HTTP request/response detail for API
tests, and trend/flakiness views across many runs.

[![CI](https://github.com/Karzone/SpecReport/actions/workflows/ci.yml/badge.svg)](https://github.com/Karzone/SpecReport/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4.svg)](https://dotnet.microsoft.com/)

</div>

---

> ⚠️ **Pre-release.** The tool works end to end and is covered by an executable conformance
> suite, but it has not been published to NuGet yet and the API may still move. See
> [Status](#status).

## What it does

Reqnroll already knows how to emit [Cucumber Messages](https://github.com/cucumber/messages).
Point SpecReport at that file:

```bash
specreport TestResults/messages.ndjson -o report.html --open
```

…and get **one self-contained HTML file** — no server, no CDN, no network calls at all — showing
every scenario as an ordered Gherkin timeline with the right evidence attached to each step.

**Frontend steps** get screenshots inline, click for a lightbox.
**Backend steps** get the exchange that step made:

```
POST  https://api.example.com/v2/orders?apiKey=●●●          503 Service Unavailable  ·  128 ms
├─ Request
│    Authorization  *** redacted ***
│    Body           { "sku": "ABC-123", "qty": 2 }
└─ Response
     x-request-id   7f3a…
     Body           { "error": "upstream_unavailable" }
     redacted: request.headers.Authorization, request.query.apiKey
```

**Several runs** get a pass-rate trend, a scenario × run matrix, and a flaky list.

## Why not the built-in formatter?

Reqnroll ships an HTML formatter, and for a plain step timeline you should just use it. This
exists for the two things it cannot do:

1. **HTTP exchange panels** — the request and response a step actually made, redacted, on the
   step that made it.
2. **Multi-run aggregation** — trend, matrix, flaky detection. The built-in formatter is
   strictly single-run.

Everything else here exists to make those two useful.

## Install

```bash
dotnet tool install -g SpecReport.Cli
```

Then enable Reqnroll's message formatter in your `reqnroll.json`:

```json
{
  "$schema": "https://schemas.reqnroll.net/reqnroll-config-latest.json",
  "formatters": {
    "message": { "outputFilePath": "specreport-messages.ndjson" }
  }
}
```

Run your tests, then:

```bash
specreport bin/Debug/net8.0/specreport-messages.ndjson -o report.html --open
```

That is the whole setup for the timeline, keywords, failures, `Rule:` grouping, data tables, and
retries. **You do not need the capture package for any of that.**

### Multiple runs

A **directory is one logical run** — every `.ndjson` inside it merges. That is what makes
sharded CI work: four agents writing four files into one folder are one run, not four.

```bash
specreport runs/monday runs/tuesday runs/wednesday -o trend.html
```

Runs are ordered by their own `testRunStarted` timestamp, never by filename or argument order.

## Adding HTTP and screenshot capture

Reference the optional package from your test project:

```bash
dotnet add package SpecReport.Capture
```

It auto-loads — no `bindingAssemblies` config needed. Then attach the handler to whichever
`HttpClient` your tests use:

```csharp
[Binding]
public sealed class ApiClient(ScenarioCapture capture)
{
    // Every exchange lands on the step that made it, even under parallel execution.
    public HttpClient Http { get; } = capture.CreateClient();
}
```

That covers every `HttpClient`-based library — Refit, RestSharp v107+, Flurl, typed clients.

For screenshots, hand it a delegate from your own driver:

```csharp
[BeforeScenario]
public void SetUp(ScenarioCapture capture) =>
    capture.UseScreenshotProvider(ct => _page.ScreenshotAsync(
        new() { Type = ScreenshotType.Jpeg, Quality = 40 }));
```

Screenshots default to **on failure only**. Registering no provider is a perfectly normal state
for an API suite — it produces no screenshots and no warnings.

### Stack-agnostic by rule

`SpecReport.Capture` depends on **Reqnroll and nothing else** — enforced by a build target that
fails if anyone adds a package reference. It will never pull a browser, HTTP framework, or JSON
serializer into your test project. Works with MSTest, NUnit, and xUnit.

## Redaction: what it covers, and what it doesn't

Captured traffic is redacted **before it reaches the `.ndjson`**, because that file gets archived
as a CI artifact — a secret that reaches the file has already leaked. On by default:

- **Headers** named `Authorization`, `Proxy-Authorization`, `Cookie`, `Set-Cookie`, `X-API-Key`,
  or matching `*token*`, `*secret*`, `*password*`, `*apikey*`, `*session*`.
- **Query-string parameters** matching the same patterns. Matching normalises case and
  separators, so `api_key`, `api-key` and `apiKey` are all caught.
- **JSON body keys** matching the same patterns, **at any nesting depth**. A sensitive key's
  whole value goes, object or array included.

**What is NOT redacted — read this before pointing it at a production-like environment:**

| Not covered | Why it matters |
|---|---|
| **Non-JSON bodies** | Form-encoded, XML, protobuf and plain-text bodies are captured verbatim. A `grant_type=password&password=…` form post is stored as sent. |
| **Secrets that are the whole body** | `POST /login` returning a bare JWT as `text/plain` has no key to match on. The value *is* the response. |
| **Values under innocuous keys** | A token under `"data"` or `"value"` looks like anything else. Name-based matching cannot know. |
| **URL path segments** | `/reset/9f3a…` is redacted only as a query parameter, not as a path segment. |
| **Anything inside a screenshot** | A logged-in page showing a session token, PII, or a payment form is captured as pixels. Nothing scans it. |
| **Your own `WriteLine` output** | Passed through untouched. |

For the gaps that matter to you: `CaptureOptions.SensitiveNameFragments` extends matching,
`CaptureOptions.BodyScrubber` gives a last-pass scrub of any body, and
`CaptureOptions.CaptureBodies = false` keeps headers and timing only.

**Treat generated reports and `.ndjson` files as sensitive by default** and scope your CI
artifact retention accordingly.

## How it is put together

| Package | Role |
|---|---|
| `SpecReport.Cli` | The `specreport` dotnet tool. Works on **any** Reqnroll project. |
| `SpecReport.Core` | Parsing and rendering, if you want to embed it. |
| `SpecReport.Capture` | Optional Reqnroll plugin: HTTP exchanges and screenshots. |

The only contract between them is Cucumber Messages — the format Reqnroll's built-in `message`
formatter already produces. SpecReport does not fork, patch, or reach into Reqnroll internals,
and the renderer works on runs recorded long before it existed.

## Status

| Milestone | |
|---|---|
| Parser — AST join, `Rule:`, Background, outlines, retries, localized Gherkin | ✅ |
| Single-run HTML with screenshot and HTTP panels | ✅ |
| Capture — HTTP attribution under parallelism, redaction, screenshots | ✅ |
| Multi-run — trend, matrix, flaky detection | ✅ |
| Polish, accessibility pass, scale budget, NuGet publish | 🚧 |

43 of 49 specification criteria are proven by an executable conformance suite: a rule with no
test **fails the build**. See [`docs/SPEC.md` §14](docs/SPEC.md) for how that works, and
[`docs/conformance.md`](docs/conformance.md) for the current board.

**Known limitations**, stated plainly:

- Nested/called steps produce no separate rows — their time and failures roll up into the outer
  step.
- Renaming a scenario reads as "removed and added" in the multi-run matrix.
- Worker/shard attribution is unavailable: Reqnroll never populates `workerId`
  ([measured](docs/reqnroll-api-notes.md)).
- Expected-vs-actual assertion diffs are not parsed; the raw failure message is shown instead.

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md). Two things are unusual and worth knowing before you
start: the conformance gate treats a missing test as a build failure, and facts about Reqnroll
are settled by recording a fixture and reading the bytes rather than by reading the docs — which
has caught genuine errors more than once.

## Licence

MIT — see [LICENSE](LICENSE).
