# Contributing to SpecReport

Thanks for taking a look. This document covers what you need to build the project and the few
rules that are stricter than usual — reading §3 before opening a PR will save you a round trip.

## Getting started

```bash
git clone https://github.com/Karzone/SpecReport.git
cd SpecReport
dotnet build
dotnet test tests/SpecReport.Conformance
```

You need the **.NET 10 SDK**. Everything else restores from NuGet.

To try the tool against the committed fixtures:

```bash
dotnet run --project src/SpecReport.Cli -- tests/fixtures/sample-run-01.ndjson -o report.html --open
```

## Project layout

| Path | What it is |
|---|---|
| `src/SpecReport.Core` | Parses Cucumber Messages into a run model and renders HTML |
| `src/SpecReport.Cli` | The `specreport` dotnet tool |
| `src/SpecReport.Capture` | Optional Reqnroll plugin: screenshots and HTTP capture |
| `tests/SpecReport.Conformance` | The conformance gate (see §3) |
| `tests/fixtures` | Recorded Reqnroll output the tests assert against |
| `samples/` | Real Reqnroll projects that produce those fixtures |
| `spikes/` | Throwaway experiments, kept for their findings |
| `docs/SPEC.md` | The specification. Start here. |

## 1. The specification is the source of truth

`docs/SPEC.md` states what SpecReport does and — just as importantly — what it deliberately
does not (§9). A change that contradicts the spec needs the spec changed first, in the same PR,
with the reasoning recorded. Several sections carry a note explaining why an earlier decision
was reversed; that is the intended pattern, not clutter.

## 2. Facts about Reqnroll come from measurement

`docs/reqnroll-api-notes.md` Part 2 records what was observed in real recorded output, including
several places where reality contradicts the documentation.

**When you have a question about how Reqnroll behaves, record a fixture and read the bytes.**
Reasoning from the docs has produced wrong answers in this project more than once — the most
consequential being that an attachment's `testStepId` cannot tell you which Gherkin step
produced it (§10). If you discover something new, add it to Part 2 with how you measured it.

## 3. The conformance gate

Every hard rule in the spec has an id in `docs/criteria.json`, and a test tagged
`[Criterion("SR-…")]` must prove it. The gate **fails the build** when a criterion at or below
`currentMilestone` has no test. That inversion is the whole point: without it, a rule nobody
implemented looks exactly like one that passes.

If you add a rule to the spec, add a criterion and a test. Three things are not acceptable:

- **Marking a criterion `manual` to avoid writing a test.** `manual` is for things that
  genuinely cannot be asserted — visual weight, contrast — and each carries a `manualNote`
  justifying it. If the list grows, that is a smell.
- **Writing an assertion that passes without proving the claim.** Asserting a CSS property
  exists does not prove "visually de-emphasised".
- **Leaving a criterion unproven.** Pushing its milestone out is fine, and honest. Silence is
  not.

## 4. `SpecReport.Capture` depends on Reqnroll and nothing else

An MSBuild target fails the build if `src/SpecReport.Capture` gains any package reference beyond
Reqnroll. This is deliberate: that assembly loads into every consumer's test process, and a
browser driver, HTTP client, or JSON serializer pulled in here would be forced on people running
API-only suites.

It is why JSON writing and redaction in that project are hand-written — `System.Text.Json` is a
NuGet package on netstandard2.0.

Need a driver? Take a delegate (see `ScreenshotProvider`). Need to see HTTP? Use a
`DelegatingHandler`.

## 5. Tests

- **Conformance tests** prove criteria. Tag them.
- **Shape guards** are untagged tests that pin overall counts. Criterion tests each check one
  narrow property, so a parser that dropped half a run could pass all of them; a shape guard
  catches that. One has already found a real error.
- **Security tests prove absence.** The redaction tests plant real-looking secrets and assert
  they are *nowhere* in the output. A test that greps for the word "redacted" would pass while
  leaking.

## Commits and pull requests

- Explain **why** in the commit message, not just what. The `git log` here is used as a design
  record and reads that way.
- Keep the build warning-free; warnings are errors in this repo.
- Run `dotnet test tests/SpecReport.Conformance` before pushing.
- One logical change per PR.

## Reporting bugs

Include the `.ndjson` that reproduces it if you can — this project is built around real
recordings, and a fixture is worth more than a description. Redact it first if it came from a
real system; see the README's note on what SpecReport's own redaction does and does not cover.

## Code of conduct

This project follows the [Contributor Covenant](CODE_OF_CONDUCT.md).
