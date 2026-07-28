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

## Licence

MIT — see [LICENSE](LICENSE).
