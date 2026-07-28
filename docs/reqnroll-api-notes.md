# Reqnroll API notes (verified 2026-07-28)

Facts checked against docs.reqnroll.net and the reqnroll/Reqnroll source on GitHub.
Version context: latest Reqnroll release **v3.3.4** (2026-03-23). Formatters require v3.0+;
output-path substitution variables require v3.3+.

## 1. Formatters (the NDJSON source SpecSight consumes)

- Built into the core `Reqnroll` package — **no extra NuGet reference needed**. Transitive
  deps: `Cucumber.Messages` 32.3.1, `Cucumber.HtmlFormatter` 23.1.0.
- Enabled via `reqnroll.json` (omitting the section disables the formatter):

```json
{
  "$schema": "https://schemas.reqnroll.net/reqnroll-config-latest.json",
  "formatters": {
    "message": { "outputFilePath": "reports\\run_{timestamp}.ndjson" }
  }
}
```

- Default output: `reqnroll_report.ndjson`, relative to the **project output folder**
  (`bin/Debug/netX/`). Substitution variables (v3.3+): `{timestamp}`, `{buildNumber}`,
  `{revision}`, `{branch}`, `{tag}`, `{env:VARNAME}`.
- Env-var overrides exist (`REQNROLL_FORMATTERS_MESSAGE=outputFilePath=...`,
  `REQNROLL_FORMATTERS_DISABLED=true`) — useful for CI without touching the repo.
- A **custom formatter** extension point exists (`ICucumberMessageFormatter`, registered via
  runtime plugin), but the docs explicitly warn the interface **may change significantly even
  in minor versions**. This confirms SPEC §9's non-goal: consume the built-in `message`
  formatter's file output; do not build on the unstable formatter API.

## 2. Envelopes Reqnroll actually emits

`meta`, `source`, `gherkinDocument`, `pickle`, `stepDefinition`, `hook`, `parameterType`,
`testRunStarted/Finished`, `testCase`, `testCaseStarted/Finished`, `testStepStarted/Finished`,
`testRunHookStarted/Finished`, `attachment`, `suggestion`. It never emits `externalAttachment`
or `parseError`.

`testStepResult`: `status` ∈ `UNKNOWN|PASSED|SKIPPED|PENDING|UNDEFINED|AMBIGUOUS|FAILED`,
`duration` = `{seconds, nanos}`, `message` (only for TestError/UndefinedStep, message text only),
`exception` = `{type, message, stackTrace}`.

### Skipped steps — better news than expected
GitHub issue #892 (`AfterStep` not firing for skipped steps) is **closed as by-design** and
will not change. But the *message stream itself is complete*: Reqnroll emits
`testStepFinished` with `SKIPPED` for steps after a failure, independent of hooks. So:
- The renderer's primary source for skips is the stream itself.
- SPEC §4.4's inference rule (steps after first failure = skipped) is a *defensive fallback*
  for incomplete streams (crashed runs), not the primary mechanism.
- The capture package must never rely on `AfterStep` firing for every step.

## 3. Attachments

- API: `IReqnrollOutputHelper` (namespace `Reqnroll`), obtained via constructor injection:
  `void AddAttachment(string filePath)` and `WriteLine(string)`.
- **Step-scoped**: Reqnroll stamps attachments with the current `testStepId` +
  `testCaseStartedId` automatically. Outside a scenario they attach to the test-run hook scope.
- `AddAttachment` reads the file and inlines it **base64 into the NDJSON** (`contentEncoding:
  BASE64`), with **no size cap and no external-file option**. Consequences:
  - Capture must keep screenshots small (JPEG, quality-capped) — every byte lands in the NDJSON.
  - The renderer/CLI is the place to externalize (`--assets external`), matching the Cucumber
    schema's intended consumer-side `ExternalAttachment` pattern.
- **Media type is inferred from the file extension** (`FileExtensionToMIMETypeMap`); the API
  offers no way to set a custom media type. So `application/vnd.specsight.http+json` is
  impossible via the public API → the evidence contract needs an **in-payload discriminator**
  (see SPEC §3.2 revision).
- `WriteLine` output becomes an attachment with `mediaType: "text/x.cucumber.log+plain"`,
  `contentEncoding: IDENTITY`.
- Runner-level display of attachments works on MSTest/NUnit; **xUnit lacks runner attachment
  support** — but the Cucumber Messages envelope is still emitted either way, which is all
  SpecSight needs.

## 4. Hooks (used by SpecSight.Capture)

- `[BeforeStep]`/`[AfterStep]`/`[BeforeScenario]`/`[AfterScenario]` — instance methods,
  constructor injection, `async Task` supported (never `async void`).
- `Order` property: lower runs first, default 10000. Tag-scoped hooks OR their tags.
- Context objects: `ScenarioContext` (→ `ScenarioInfo.CombinedTags` — use this, it includes
  feature-inherited tags; `ScenarioExecutionStatus`; `TestError`), `ScenarioStepContext`
  (→ `StepInfo`, `Status`, `StepError`).
- ⚠️ `StepInfo` has no `Keyword` property — the Gherkin keyword lives on
  `StepInfo.StepInstance.Keyword`. `StepInfo.PickleStepId` is `internal` — not accessible
  without reflection. We don't need it: attachment→step correlation is handled by Reqnroll
  (see §3), so Capture never joins ids itself.

## 5. Runtime plugin packaging (SpecSight.Capture)

- Implement `Reqnroll.Plugins.IRuntimePlugin` + `[assembly: RuntimePlugin(typeof(...))]`.
- **The assembly file name must end in `.ReqnrollPlugin.dll`** or Reqnroll won't auto-load it.
  → assembly name `SpecSight.Capture.ReqnrollPlugin`, NuGet package id stays `SpecSight.Capture`.
- Target **netstandard2.0** for maximum consumer compatibility.
- DI hooks: `RegisterGlobalDependencies` (add new), `CustomizeScenarioDependencies`
  (per-scenario services — where the HTTP capture handler's step-context accessor lives).
- Note: plain hook classes in a referenced assembly also need
  `bindingAssemblies` config in the consumer's reqnroll.json — the runtime-plugin route with
  `CustomizeScenarioDependencies` avoids forcing config on consumers. Decide in M3 design.
