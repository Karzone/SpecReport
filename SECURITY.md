# Security Policy

## Supported versions

SpecReport is pre-1.0. Only the latest release receives fixes.

| Version | Supported |
|---|---|
| 0.1.x | ✅ |
| < 0.1 | ❌ |

## Reporting a vulnerability

**Please do not open a public issue for a security problem.**

Use [GitHub's private vulnerability reporting](https://github.com/Karzone/SpecReport/security/advisories/new),
which notifies the maintainer privately and gives us a place to coordinate a fix.

Please include what you were running, what you observed, and a reproduction if you have one. If
a `.ndjson` demonstrates it, redact anything real before sending — and see the caveat below
about what SpecReport's own redaction does not cover.

You can expect an acknowledgement within a few days. As a single-maintainer project there is no
formal SLA beyond that.

## What counts as a vulnerability here

SpecReport reads test-run output and writes an HTML file. The realistic risks are about
**content leaving where it should not**, so these are in scope:

- **A secret reaching the `.ndjson` or the report** that the documented redaction rules say
  should have been removed — a matching header, query parameter, or JSON body key that survives.
- **Script execution in a generated report.** Run content is escaped and rendered as text; a way
  to get scenario names, step text, failure messages, or HTTP bodies to execute as markup or
  script is a vulnerability. There is a conformance test for this (`SR-R2`), so a bypass of it
  is worth reporting.
- **The report reaching the network.** A generated report must issue zero network requests; a
  way to make it call out would defeat the guarantee people rely on when opening these files on
  locked-down machines (`SR-R1`).
- **Path traversal** when writing a report or external assets.

## What is *not* a vulnerability

These are documented limitations, not bugs. The README's
[redaction section](README.md#redaction-what-it-covers-and-what-it-doesnt) states them plainly:

- Secrets in **non-JSON bodies** (form-encoded, XML, plain text) are captured verbatim.
- A secret that **is** the whole response body — a login returning a bare JWT — has no key to
  match on.
- Values under **innocuous key names** (`data`, `value`) are not detected. Matching is by name.
- Secrets in **URL path segments** rather than query parameters.
- Anything visible **inside a screenshot**. Nothing scans image content.

Redaction is name-based and best-effort. **Treat generated reports and `.ndjson` files as
sensitive by default** and scope your CI artifact retention accordingly.

## A note on the test fixtures

`tests/fixtures/` contains strings that look like credentials —
`SPECREPORT-MUST-NOT-LEAK-BEARER-8f3a91` and similar. These are **invented values**, planted
deliberately so the redaction tests can prove they never reach the output. They are not real
credentials and grant access to nothing.
