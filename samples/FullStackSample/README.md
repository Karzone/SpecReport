# Full-stack sample

A single Reqnroll suite containing **both** backend API tests and Playwright browser tests, so
one report carries HTTP exchange panels and screenshots side by side.

```bash
dotnet test samples/FullStackSample
specreport samples/FullStackSample/bin/Debug/net10.0/fullstack-messages.ndjson -o report.html --open
```

Three of the fifteen scenarios fail on purpose — a report where nothing fails demonstrates
almost nothing.

## What it demonstrates

| | |
|---|---|
| `Features/Api.feature` | Backend only. Every step's request and response, redacted, on the step that made it. |
| `Features/Ui.feature` | Frontend only. Real Chromium; screenshots on failure. |
| `Features/FullStack.feature` | **Both in one scenario** — the case the toolkit exists for. |

The interesting one is the third. `An item added through the API shows up in the storefront`
posts to the API and then asserts on the rendered page, and its timeline shows the HTTP exchange
*and* the browser state, with no special casing anywhere.

It also shows capture is library-agnostic: `RestSharp traffic is captured exactly like
HttpClient` proves it, because RestSharp — like Refit, Flurl, and typed clients — sits on
`HttpMessageHandler` underneath, which is where SpecReport hooks in.

## Why the app is hosted in-process

`App/DemoShop.cs` is a real ASP.NET app serving an HTML storefront and a JSON API, started once
per run on an OS-assigned port.

Pointing the UI tests at a live website would make this sample slow, flaky, and dependent on
somebody else's uptime — and the point is to show what the *report* looks like, not to test
wikipedia.org. Everything here runs offline.

Baskets are keyed by an `X-Session` header shared between the API client and the browser
context, because scenarios run in parallel against one shared host. A single global basket made
them race: one scenario's "add to basket" surfaced in another's assertion. That is a realistic
shape for a test environment, not an artefact of the sample.

## What it taught us

Two bugs in SpecReport itself were found by building this, both by *looking at the output*
rather than trusting it:

1. **`CreateHandler()` returned a handler with no inner handler**, so the obvious
   `new HttpClient(capture.CreateHandler())` died with "The inner handler has not been assigned"
   — a message that tells you nothing about the fix.
2. **Screenshots rendered against the wrong step.** One taken after the failing step (5 of 5)
   appeared on step 1, because Reqnroll stamps an `[AfterStep]` attachment with the hook's step
   id. `SR-C9` now guards it.

And one documented gap surfaced the hard way: the login scenario originally wrote a password
into the Gherkin. Redaction handled the traffic perfectly — the request body shows
`"password":"*** redacted ***"` — but step text renders as authored, because it is part of the
test rather than the traffic. The credential now lives in the step definition.
