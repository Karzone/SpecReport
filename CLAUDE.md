# CLAUDE.md — SpecReport

Guardrails for this repository. These override default behaviour.

---

## 1. Attribution — the author is always Karzone

**Every commit is authored and committed as `Karzone <karthikawaiting@gmail.com>`.** No
exceptions.

- **Never** add a `Co-Authored-By:` trailer naming Claude, an AI, or any assistant.
- **Never** add "Generated with", "Written by Claude", or similar to a commit message, PR
  description, code comment, file header, or documentation.
- **Never** set `user.name` / `user.email` to anything else, per-command or in config.
- The same applies to anything outward-facing: PR/issue text, release notes, package metadata,
  and the `Authors` / `Copyright` fields in `Directory.Build.props`.

Verify before pushing:

```bash
git log --format="%h A:%an C:%cn" | sort -u
```

Every line must read `A:Karzone C:Karzone`. If one does not, fix it before pushing — history
rewriting is far cheaper before the commit leaves the machine than after.

## 2. Visibility

This repository is **private** while the tool is being proven. Do not make it public, publish
packages to nuget.org, or push to any other remote without an explicit instruction saying so.

`SPEC.md` §12 question 4 (public NuGet vs internal feed) is **decided for now: private**.
Revisit only when asked.

## 3. The conformance gate is the definition of done

Read `docs/SPEC.md` §14 before touching `docs/criteria.json`.

- A criterion at or below `currentMilestone` **must** have a passing test tagged
  `[Criterion("SR-…")]`, or the build fails. That is the point — missing evidence is a failure,
  not a silent pass.
- Declaring a milestone complete = bumping `currentMilestone` and getting green.
- **Never** mark a criterion `manual` to avoid writing a test, and never write an assertion that
  passes without proving the claim. If a criterion cannot be met, revise it *with the reasoning
  recorded* (see SR-S6 for the worked example) or push its milestone out. Silence is the thing
  being forbidden.

## 4. SpecReport.Capture depends on Reqnroll and nothing else

`src/SpecReport.Capture` may reference only `Reqnroll` and the BCL — enforced by an MSBuild
target that fails the build, and by `SR-C6`. A driver or HTTP library here would be forced on
every consumer, including API-only suites. Use a provider delegate (screenshots) or a
`DelegatingHandler` (HTTP) instead.

This is why `System.Text.Json` is hand-rolled in that project: it is a NuGet package on
netstandard2.0.

## 5. Facts about Reqnroll come from measurement

`docs/reqnroll-api-notes.md` Part 2 records what was observed in real recorded output, and
where it contradicts the documentation. Several load-bearing design decisions rest on it —
notably that `testStepId` cannot identify which Gherkin step produced an attachment (§10).

When a new question about Reqnroll behaviour comes up, **record a fixture and check the bytes**
rather than reasoning from the docs. That has caught a genuine error three times in this
project's short history.
