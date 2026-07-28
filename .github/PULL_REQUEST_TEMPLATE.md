<!--
Thanks for contributing. CONTRIBUTING.md §3 covers the conformance gate, which is the one thing
about this repo that surprises people. The short version: if you add a rule, add a criterion and
a test that proves it.
-->

## What this changes

<!-- And why. Commit messages here are used as a design record - the same detail is welcome. -->

## Checklist

- [ ] `dotnet build` is warning-free (warnings are errors in this repo)
- [ ] `dotnet test tests/SpecReport.Conformance` passes
- [ ] If this adds or changes a rule, `docs/SPEC.md` is updated in the same PR
- [ ] If this adds a rule, `docs/criteria.json` has a criterion and a test tags it with `[Criterion("SR-…")]`
- [ ] If this changes something about Reqnroll's behaviour, it was verified against a recorded fixture rather than the docs (`docs/reqnroll-api-notes.md`)
- [ ] `src/SpecReport.Capture` gained no package reference beyond Reqnroll

## Anything you are unsure about

<!-- Genuinely useful. Say so rather than guessing. -->
