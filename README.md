<div align="center">

<img src="assets/icon.png" alt="" width="88" height="88">

# SpecReport

**HTML reports for [Reqnroll](https://reqnroll.net) test runs.**

Gherkin step timelines with screenshots for UI tests, full HTTP request/response detail for API
tests, and trend + flakiness views across many runs.

*One self-contained file · No server · No CDN · Opens offline*

[![NuGet](https://img.shields.io/nuget/vpre/SpecReport.Cli?logo=nuget&label=SpecReport.Cli)](https://www.nuget.org/packages/SpecReport.Cli)
[![CI](https://github.com/Karzone/SpecReport/actions/workflows/ci.yml/badge.svg)](https://github.com/Karzone/SpecReport/actions/workflows/ci.yml)
[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4.svg)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

[Quick start](#quick-start) ·
[What you get](#what-you-get) ·
[Multiple runs](#multiple-runs) ·
[Capture](#capturing-http-and-screenshots) ·
[CI](#running-it-in-ci) ·
[CLI](#cli-reference) ·
[Redaction](#redaction-what-it-covers-and-what-it-doesnt) ·
[Troubleshooting](#troubleshooting) ·
[Limitations](#limitations)

</div>

---

> **Pre-release (`0.1.0-alpha`).** It works end to end and every rule in the specification is
> covered by an executable conformance suite — but the API may still move before 1.0.

## Quick start

Three steps, about two minutes. **No package reference needed** — this works on any existing
Reqnroll 3.0+ project.

### 1. Install the tool

```bash
dotnet tool install -g SpecReport.Cli --prerelease
```

### 2. Tell Reqnroll to emit Cucumber Messages

Add a `reqnroll.json` to your test project (or edit the one you have). Set it to copy to the
output directory.

```json
{
  "$schema": "https://schemas.reqnroll.net/reqnroll-config-latest.json",
  "formatters": {
    "message": { "outputFilePath": "specreport-messages.ndjson" }
  }
}
```

<details>
<summary>Make sure it copies to output (click if you use a plain <code>.csproj</code>)</summary>

```xml
<ItemGroup>
  <None Update="reqnroll.json" CopyToOutputDirectory="PreserveNewest" />
</ItemGroup>
```
</details>

### 3. Run your tests, then render

```bash
dotnet test
```

```bash
specreport bin/Debug/net8.0/specreport-messages.ndjson -o report.html --open
```

That's it. Open `report.html` in any browser — it is a single self-contained file with no
server, no CDN, and no network calls at all.

## What you get

**Without any code changes** (the quick start above):

- Every scenario as an ordered Gherkin timeline — real keywords, in the feature's own language
- `Rule:` grouping, Background steps marked as such, Scenario Outline example values
- Failures **expanded by default**, with exception type, message, and stack trace
- Hook rows, so a failing `BeforeScenario` is visible instead of showing as "everything skipped"
- Retries grouped into one scenario, with earlier attempts nested
- Filters by status and tag, plus full-text search across scenarios and steps
- Light/dark theme, following your OS with a toggle to override

**With the optional [`SpecReport.Capture`](#capturing-http-and-screenshots) package**, each step
also carries the evidence it produced:

```
POST  https://api.example.com/v2/orders?apiKey=●●●     503 Service Unavailable  ·  128 ms
├─ Request
│    Authorization  *** redacted ***
│    Body           { "sku": "ABC-123", "qty": 2 }
└─ Response
     x-request-id   7f3a…
     Body           { "error": "upstream_unavailable" }
     redacted: request.headers.Authorization, request.query.apiKey
```

### Why not Reqnroll's built-in formatter?

Reqnroll ships an `html` formatter, and for a plain step timeline you should just use it. This
exists for the two things it cannot do:

1. **HTTP exchange panels** — the request and response a step actually made, redacted, attached
   to the step that made it.
2. **Multi-run aggregation** — trend, matrix, flaky detection. The built-in formatter is
   strictly single-run.

## Multiple runs

**A directory is one logical run** — every `.ndjson` inside it merges. That is what makes
sharded CI work: four agents writing four files into one folder are *one* run, not four.

```bash
specreport runs/monday runs/tuesday runs/wednesday -o trend.html
```

You get a pass-rate trend, a scenario × run matrix, a flaky list, and a drill-down into any
run's full timeline. Runs are ordered by their own `testRunStarted` timestamp — never by
filename or the order you typed them.

A blank matrix cell means **did not run**, which is deliberately distinct from *skipped* and is
excluded from flakiness: a scenario added last week is not flaky for not having existed.

## Capturing HTTP and screenshots

```bash
dotnet add package SpecReport.Capture --prerelease
```

It auto-loads on reference — no `bindingAssemblies` configuration needed.

### HTTP

Ask the injected `ScenarioCapture` for a client and use it as normal:

```csharp
[Binding]
public sealed class ApiClient(ScenarioCapture capture)
{
    // Every exchange lands on the step that made it, even under parallel execution.
    public HttpClient Http { get; } = capture.CreateClient();
}
```

Already have a configured handler chain? Wrap it instead:

```csharp
var handler = capture.CreateHandler();      // a DelegatingHandler
handler.InnerHandler = yourExistingHandler;
```

This covers **every `HttpClient`-based library** — Refit, RestSharp v107+, Flurl, typed clients,
`IHttpClientFactory`.

### Screenshots

Hand it a delegate from your own driver. SpecReport never references one itself:

```csharp
// Playwright — ask for JPEG, which is what the size budget expects
[BeforeScenario]
public void SetUp(ScenarioCapture capture) =>
    capture.UseScreenshotProvider(ct => _page.ScreenshotAsync(
        new() { Type = ScreenshotType.Jpeg, Quality = 40 }));
```

```csharp
// Selenium
capture.UseScreenshotProvider(ct => Task.FromResult(
    ((ITakesScreenshot)_driver).GetScreenshot().AsByteArray));
```

Screenshots default to **on failure only**. Registering no provider is a perfectly normal state
for an API suite — it produces no screenshots and no warnings.

### Tuning

```csharp
capture.UseOptions(new CaptureOptions
{
    MaxBodyBytes = 32 * 1024,               // default 64 KB
    CaptureBodies = false,                  // headers and timing only
    BodyScrubber = body => Scrub(body),     // your own last-pass redaction
    Screenshots = { Mode = ScreenshotMode.EveryStep, MaxBytes = 1_000_000 }
});
```

### Stack-agnostic by rule

`SpecReport.Capture` depends on **Reqnroll and nothing else**, enforced by a build target that
fails if anyone adds a package reference. It will never pull a browser, HTTP framework, or JSON
serializer into your test project. Works with MSTest, NUnit, and xUnit.

## Running it in CI

<details open>
<summary><b>GitHub Actions</b></summary>

```yaml
- name: Test
  run: dotnet test --logger trx
  continue-on-error: true          # still publish a report when tests fail

- name: Report
  run: |
    dotnet tool install -g SpecReport.Cli --prerelease
    specreport '**/bin/**/specreport-messages.ndjson' -o report.html

- uses: actions/upload-artifact@v4
  with:
    name: test-report
    path: report.html
```
</details>

<details>
<summary><b>Azure Pipelines</b></summary>

```yaml
- script: dotnet test
  continueOnError: true

- script: |
    dotnet tool install -g SpecReport.Cli --prerelease
    specreport $(Build.SourcesDirectory)/**/specreport-messages.ndjson -o $(Build.ArtifactStagingDirectory)/report.html

- task: PublishBuildArtifacts@1
  inputs:
    pathToPublish: $(Build.ArtifactStagingDirectory)
    artifactName: test-report
```
</details>

`specreport` **exits 0 even when tests failed** — your test step already failed the build, and
failing again here would only deny you the report. It exits `2` only when there is nothing to
do: no input files, or no readable envelopes.

To keep a history and get the trend view, archive each run's `.ndjson` into a dated folder and
point `specreport` at the folders.

## CLI reference

```
specreport <paths...> [options]
```

`<paths>` accepts files, globs, or directories. **A directory is one logical run** and its
`.ndjson` files merge; several paths give you a multi-run report.

| Option | Default | |
|---|---|---|
| `-o, --output <path>` | `specreport.html` | Where to write the report |
| `--title <text>` | derived | Report title |
| `--assets <inline\|external>` | `inline` | `external` writes images to `<output>_files/` |
| `--open` | off | Open the report when done |
| `-h, --help` | | Usage |

| Exit code | |
|---|---|
| `0` | Report produced — including with warnings, and including when tests failed |
| `2` | Nothing to do: no input files, or no readable envelopes |

## Redaction: what it covers, and what it doesn't

Captured traffic is redacted **before it reaches the `.ndjson`**, because that file gets archived
as a CI artifact — a secret that reaches the file has already leaked. On by default:

- **Headers** named `Authorization`, `Proxy-Authorization`, `Cookie`, `Set-Cookie`, `X-API-Key`,
  or matching `*token*`, `*secret*`, `*password*`, `*apikey*`, `*session*`.
- **Query-string parameters** matching the same patterns. Matching normalises case and
  separators, so `api_key`, `api-key`, and `apiKey` are all caught.
- **JSON body keys** matching the same patterns, **at any nesting depth**. A sensitive key's
  whole value goes, object or array included.

**What is NOT redacted — read this before pointing it at a production-like environment:**

| Not covered | Why it matters |
|---|---|
| **Non-JSON bodies** | Form-encoded, XML, protobuf, and plain-text bodies are captured verbatim. A `grant_type=password&password=…` form post is stored as sent. |
| **Secrets that are the whole body** | `POST /login` returning a bare JWT as `text/plain` has no key to match on. The value *is* the response. |
| **Values under innocuous keys** | A token under `"data"` or `"value"` looks like anything else. Name-based matching cannot know. |
| **URL path segments** | `/reset/9f3a…` is redacted as a query parameter, not as a path segment. |
| **Anything inside a screenshot** | A logged-in page showing a session token, PII, or a payment form is captured as pixels. Nothing scans it. |
| **Secrets written in your feature files** | Step text is rendered exactly as authored. `Given I log in with password "hunter2"` appears in the report because it is part of the *test*, not the traffic. Keep credentials in step definitions or configuration. |
| **Your own `WriteLine` output** | Passed through untouched. |

Use `CaptureOptions.SensitiveNameFragments` to extend matching, `BodyScrubber` for a last-pass
scrub of any body, or `CaptureBodies = false` to keep headers and timing only.

**Treat generated reports and `.ndjson` files as sensitive by default** and scope your CI
artifact retention accordingly.

## Troubleshooting

<details>
<summary><b>No <code>.ndjson</code> file appears after <code>dotnet test</code></b></summary>

The `message` formatter is off unless `reqnroll.json` names it, and the file is written relative
to the **project output folder** (`bin/Debug/netX/`), not the project root. Check that
`reqnroll.json` copies to output — omitting `CopyToOutputDirectory` is the usual cause. Formatters
need **Reqnroll 3.0+**.
</details>

<details>
<summary><b>The report has a timeline but no HTTP panels</b></summary>

Traffic is only captured if it goes through a client `SpecReport.Capture` is attached to. Use
`capture.CreateClient()` or wrap your handler with `capture.CreateHandler()`. A client built
once at application startup by `IHttpClientFactory` needs the handler registered there.
</details>

<details>
<summary><b>Screenshots are missing on a failed step</b></summary>

Screenshots need a registered provider — SpecReport cannot take one itself. Check that
`UseScreenshotProvider` runs before the step. If your provider returns a large PNG it may exceed
the 512 KB budget and be dropped; the report notes when that happens. Return JPEG, or raise
`ScreenshotOptions.MaxBytes`.
</details>

<details>
<summary><b>The report says "Incomplete input"</b></summary>

The stream ended early — usually a CI timeout or a crashed test host. The report is still
rendered from everything that was recorded, and the banner says what was lost. Steps whose
status the parser had to infer are marked `inferred`.
</details>

<details>
<summary><b>A scenario appears twice in the multi-run matrix</b></summary>

Scenario identity is feature URI + rule + name + example row. Renaming a scenario reads as one
disappearing and another appearing. This is a known limitation.
</details>

## Limitations

Stated plainly, so you meet them here rather than later:

- **Very large runs produce a large report.** Measured: 5,000 scenarios / 50,000 steps generates
  in about 2.4 s but yields a **13 MB** file, which takes a moment to open. Up to roughly 2,000
  scenarios (~5 MB) is comfortable. Beyond that, render each run separately and use the
  multi-run view, or pass `--assets external`.
- **Nested/called steps** produce no separate rows — their time and failures roll up into the
  outer step.
- **Renaming a scenario** reads as "removed and added" in the multi-run matrix.
- **No worker/shard attribution** — Reqnroll never populates `workerId`
  ([measured](docs/reqnroll-api-notes.md)). Use a `runmeta` attachment to record shard identity.
- **No expected-vs-actual diffs** — the raw failure message is shown rather than parsed, because
  parsing it reliably means special-casing every assertion library.

## How it fits together

| Package | Role |
|---|---|
| [`SpecReport.Cli`](https://www.nuget.org/packages/SpecReport.Cli) | The `specreport` dotnet tool. Works on **any** Reqnroll project. |
| [`SpecReport.Core`](https://www.nuget.org/packages/SpecReport.Core) | Parsing and rendering, if you want to embed it. |
| [`SpecReport.Capture`](https://www.nuget.org/packages/SpecReport.Capture) | Optional Reqnroll plugin: HTTP exchanges and screenshots. |

The only contract between them is [Cucumber Messages](https://github.com/cucumber/messages) —
the format Reqnroll's built-in `message` formatter already produces. SpecReport does not fork,
patch, or reach into Reqnroll internals, and the renderer works on runs recorded long before it
existed.

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md). Two things are unusual and worth knowing first: the
conformance gate treats a **missing test as a build failure**, and facts about Reqnroll are
settled by recording a fixture and reading the bytes rather than reading the docs — which has
caught genuine errors more than once.

All 49 specification criteria are satisfied: 46 proven by tests, 3 signed off by a human, and
which is which is [recorded](docs/conformance.md).

## Licence

MIT — see [LICENSE](LICENSE).
