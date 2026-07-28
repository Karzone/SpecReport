# Reqnroll API notes (verified 2026-07-28)

Facts checked against docs.reqnroll.net and the reqnroll/Reqnroll source on GitHub.
Version context: latest Reqnroll release **v3.3.4** (2026-03-23). Formatters require v3.0+;
output-path substitution variables require v3.3+.

## 1. Formatters (the NDJSON source SpecReport consumes)

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
  offers no way to set a custom media type. So `application/vnd.specreport.http+json` is
  impossible via the public API → the evidence contract needs an **in-payload discriminator**
  (see SPEC §3.2 revision).
- `WriteLine` output becomes an attachment with `mediaType: "text/x.cucumber.log+plain"`,
  `contentEncoding: IDENTITY`.
- Runner-level display of attachments works on MSTest/NUnit; **xUnit lacks runner attachment
  support** — but the Cucumber Messages envelope is still emitted either way, which is all
  SpecReport needs.

## 4. Hooks (used by SpecReport.Capture)

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

## 5. Runtime plugin packaging (SpecReport.Capture)

- Implement `Reqnroll.Plugins.IRuntimePlugin` + `[assembly: RuntimePlugin(typeof(...))]`.
- **The assembly file name must end in `.ReqnrollPlugin.dll`** or Reqnroll won't auto-load it.
  → assembly name `SpecReport.Capture.ReqnrollPlugin`, NuGet package id stays `SpecReport.Capture`.
- Target **netstandard2.0** for maximum consumer compatibility.
- DI hooks: `RegisterGlobalDependencies` (add new), `CustomizeScenarioDependencies`
  (per-scenario services — where the HTTP capture handler's step-context accessor lives).
- Note: plain hook classes in a referenced assembly also need
  `bindingAssemblies` config in the consumer's reqnroll.json — the runtime-plugin route with
  `CustomizeScenarioDependencies` avoids forcing config on consumers. Decide in M3 design.

---

# Part 2 — Observed from a real run (M0, 2026-07-28)

Everything above was read from docs and source. Everything below was **measured** against
`tests/fixtures/sample-run-01.ndjson` (Reqnroll 3.3.4 / MSTest / .NET 10, protocolVersion
30.1.0). Where the two disagree, this section wins.

## 6. Confirmed as assumed

| Assumption | Result |
|---|---|
| Attachments are step-scoped | ✅ All 10 attachments carry **both** `testStepId` and `testCaseStartedId` |
| Media type inferred from file extension | ✅ `.png`→`image/png`, `.json`→`application/json`. No way to set a custom one → the in-payload `$specreport` discriminator (SPEC §3.2) is necessary, not merely convenient |
| `WriteLine` output | ✅ `text/x.cucumber.log+plain`, `contentEncoding: IDENTITY`. **One attachment envelope per `WriteLine` call** |
| Binary attachments | ✅ `contentEncoding: BASE64`, whole file inlined, no size cap |
| Skipped steps reach the stream | ✅ 7 × `testStepFinished` with `SKIPPED`, despite step hooks not firing for them. SPEC §4.4's inference stays a fallback |
| Failure detail | ✅ `exception: { type, message, stackTrace }`; `type` is the real exception class name (`BasketAssertionException`), so it is worth surfacing |
| Hook steps are distinguishable | ✅ `testCase.testSteps[]` entries carry **either** `pickleStepId` **or** `hookId` — never both. SPEC §4.3 hook rows are implementable exactly as designed |
| `meta` has no timestamp | ✅ Run time comes from `testRunStarted.timestamp` (+ it also carries an `id`) |
| Pickle steps carry no keyword | ✅ Only `text`, `type` (Context/Action/Outcome), and `astNodeIds` → the `gherkinDocument` AST join is **mandatory**, as SPEC §4.3 assumed |
| `Rule:` needs its own grouping slot | ✅ Appears as `feature.children[].rule`, with its **own** `children` (including a per-rule Background). One nesting level |

## 7. Differs from what the docs implied — spec-affecting

1. **`hook.name` is empty.** The `hook` envelope carries `id`, `type`
   (`BEFORE_TEST_RUN` / `BEFORE_TEST_CASE` / `AFTER_TEST_CASE` / `AFTER_TEST_RUN`), and
   `tagExpression` — but **no method name**. Hook rows in the timeline therefore cannot be
   labelled "FailingBeforeHook". Label from type + tag expression instead:
   `Before hook` / `Before hook (@fail-before-hook)`.
2. **Gherkin-derived ids are deterministic; execution ids are not.** Measured across two
   consecutive runs: all 28 `pickle.id`s identical, all 28 `testCase.id`s different.
   Pickle/AST ids are content hashes (32-char hex); execution ids are random per run
   (22-char base64url). Consequence for SPEC §6.2: derived identity
   (URI + rule + name + params) stays **primary** because it survives step-text edits, but
   `pickle.id` is a valid corroborating key and a fast path when feature files are unchanged.
3. **Outline example rows are directly identifiable.** An outline pickle's `astNodeIds` is
   `[scenarioNodeId, exampleRowId]` — the row id is right there, and **two byte-identical
   example rows get different ids**. SPEC §6.2's "example-row index tiebreaker" is therefore
   cheaper than expected: use `astNodeIds[1]`. Keep the positional index as the fallback for
   streams where it is absent.
4. **AST step keywords carry a trailing space** — `"Given "`, `"Angenommen "`. Trim for
   display. `keywordType` (`Context`/`Action`/`Outcome`/`Conjunction`) is the styling key, as
   SPEC §4.3 specified — verified against German: `Angenommen`→Context, `Wenn`→Action,
   `Dann`→Outcome.
5. **`meta.ci` is absent** on a local run. The report must degrade gracefully rather than
   assume commit/branch/build metadata exists — which is exactly why the `runmeta/1`
   attachment (SPEC §3.1) earns its place.
6. **`testCaseStarted.attempt` exists (`0`), but `workerId` is NEVER populated — including
   under parallel execution.** The sample runs with
   `[assembly: Parallelize(Scope = ExecutionScope.MethodLevel)]`, and the fixture proves the
   run really was concurrent: `testCaseStarted` timestamps arrive **out of monotonic order**
   (…716830400 then …706765000), which only happens across threads. Yet all 28 entries lack
   `workerId`. **Conclusion: no worker/shard attribution is available from Reqnroll today.**
   Any "which worker ran this" UI is off the table until Reqnroll emits it; shard identity has
   to come from the `runmeta/1` attachment instead (SPEC §3.1).
7. **The fixture is a parallel-execution fixture.** That is deliberate and worth keeping: it
   means the parser is exercised against interleaved `testStepStarted`/`testStepFinished`
   envelopes from the very first test, rather than a tidy sequential stream that would hide
   ordering bugs until much later.

## 8. Opportunities noticed

- **`suggestion` envelope**: for undefined steps Reqnroll emits a ready-made C# step-definition
  snippet. Rendering "here's the binding you're missing" directly in the report is a cheap,
  genuinely useful feature the built-in HTML formatter doesn't surface. Candidate for M3.
- **Build-time `.ndjson` files** appear in `obj/Debug/<tfm>/Features/*.feature.ndjson` — these
  are per-feature Gherkin artifacts from code generation, *not* the runtime stream. Don't
  confuse them with the formatter output; `.gitignore` excludes them.

## 9. Environment gotcha

`InvariantGlobalization=true` **breaks Reqnroll at startup**:
`ConfigurationLoader.DefaultFeatureLanguage` calls `CultureInfo.GetCultureInfo("en-us")`, which
throws under invariant mode and aborts the whole run before a single scenario executes. Culture
data is load-bearing for any Reqnroll consumer — and doubly so for SpecReport, which supports
localized Gherkin. `Directory.Build.props` pins it to `false` with a comment.
