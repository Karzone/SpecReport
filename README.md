# SpecReport

**A rich HTML report for [Reqnroll](https://reqnroll.net) test runs — Gherkin step timelines with
screenshots for UI tests and full HTTP request/response detail for API tests. One run or many.**

> 🚧 **Status: design phase.** Nothing is implemented yet. The specification is in
> [`docs/SPEC.md`](docs/SPEC.md) and is the thing to read (and argue with) first.

---

## What it will do

Point it at the Cucumber Messages file Reqnroll already knows how to emit:

```bash
specreport report TestResults/*.ndjson -o report.html
```

…and get one self-contained HTML file — no server, no CDN, no network calls — showing every
scenario as an ordered Gherkin timeline, with the right evidence hanging off each step:

- **Frontend steps** → screenshots inline, click for a lightbox
- **Backend steps** → method, URL, status, latency, request/response headers and bodies
- **Multiple runs** → pass-rate trend, a scenario × run status matrix, and a flaky-scenario list

## How it's put together

Two independently useful pieces:

| Package | Role |
|---|---|
| `SpecReport.Cli` | `dotnet tool` that turns Cucumber Messages NDJSON into HTML. Works on **any** Reqnroll project — you do not need the capture package. |
| `SpecReport.Capture` | Optional. Reference it from your test project and it attaches screenshots and HTTP exchanges to the right steps automatically. |

The only contract between them is [Cucumber Messages](https://github.com/cucumber/messages) —
the format Reqnroll's built-in `message` formatter already produces. SpecReport does not fork,
patch, or reach into Reqnroll internals.

**Stack-agnostic by rule:** works with any test runner (MSTest, NUnit, xUnit) and any
UI/HTTP framework. Screenshots come from a one-line provider delegate you register (Playwright,
Selenium, anything that yields bytes); HTTP capture is a standard `DelegatingHandler`, which
covers every `HttpClient`-based library (Refit, RestSharp v107+, Flurl, typed clients).
`SpecReport.Capture` itself depends on Reqnroll and nothing else — it will never pull a
browser or HTTP framework into your test project.

## Redaction: what it covers, and what it doesn't

Captured traffic is redacted **before it reaches the NDJSON**, because that file gets archived
as a CI artifact — a secret that reaches the file has already leaked. On by default:

- **Headers** named `Authorization`, `Proxy-Authorization`, `Cookie`, `Set-Cookie`, `X-API-Key`,
  or matching `*token*`, `*secret*`, `*password*`, `*apikey*`, `*session*`.
- **Query-string parameters** matching the same patterns — `?api_key=`, SAS tokens,
  password-reset links. Matching normalises case and separators, so `api_key`, `api-key` and
  `apiKey` are all caught.
- **JSON body keys** matching the same patterns, **at any nesting depth**. A sensitive key's
  whole value goes, object or array included.

**What is NOT redacted — read this before pointing it at a production-like environment:**

| Not covered | Why it matters |
|---|---|
| **Non-JSON bodies** | Form-encoded, XML, protobuf and plain-text bodies are captured verbatim. A `grant_type=password&password=…` form post is stored as sent. |
| **Secrets that are the whole body** | `POST /login` returning a bare JWT as `text/plain` has no key to match on. The value *is* the response. |
| **Values under innocuous keys** | A token under `"data"` or `"value"` looks like anything else. Name-based matching cannot know. |
| **URL path segments** | `/reset/9f3a…` is redacted only if it appears as a query parameter, not as a path segment. |
| **Anything inside a screenshot** | A logged-in page showing a session token, PII, or a payment form is captured as pixels. Nothing scans it. |
| **Request/response bodies your own steps log** | `WriteLine` output is passed through untouched. |

For the gaps that matter to you, use the escape hatches: `CaptureOptions.SensitiveNameFragments`
to extend matching, `CaptureOptions.BodyScrubber` for a last-pass scrub of any body, or
`CaptureOptions.CaptureBodies = false` to keep headers and timing only.

Treat generated reports and `.ndjson` files as **sensitive by default** and scope your CI
artifact retention accordingly.

## Licence

MIT — see [LICENSE](LICENSE).
