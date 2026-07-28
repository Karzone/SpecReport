using System.Net.Http.Headers;
using CaptureSampleTests.Support;
using Reqnroll;
using SpecReport.Capture;

namespace CaptureSampleTests.Steps;

/// <summary>
/// Consumes SpecReport.Capture the way a real API test suite would: ask the injected
/// ScenarioCapture for a client and use it. No ambient plumbing, no ids to correlate.
/// </summary>
[Binding]
public sealed class AttributionSteps(ScenarioCapture capture, ScenarioClient client)
{
    /// <summary>
    /// The URL carries ground truth. The conformance gate later checks that each captured
    /// exchange landed on a step whose text contains the same token.
    /// </summary>
    [Given(@"step (.*) makes a call")]
    [When(@"step (.*) makes a call")]
    [Then(@"step (.*) makes a call")]
    public async Task StepMakesACall(string stepToken)
    {
        await Task.Yield();
        var response = await client.Http.GetAsync($"/probe/{stepToken}");
        response.EnsureSuccessStatusCode();
    }

    [Given(@"step (.*) makes a call carrying secrets")]
    public async Task StepMakesACallCarryingSecrets(string stepToken)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            // A secret in the query string - the case people forget, since it is not a header.
            $"/probe/{stepToken}?api_key={Secrets.ApiKey}&page=2");

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Secrets.BearerToken);
        request.Headers.TryAddWithoutValidation("X-Correlation-Id", "corr-0001");
        request.Content = new StringContent("""{"sku":"ABC-123","qty":2}""");

        var response = await client.Http.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// A secret four levels down, under an innocuous-looking parent. Name-based redaction has
    /// to walk the whole document, not just its top level.
    /// </summary>
    [Given(@"step (.*) posts a deeply nested secret")]
    public async Task StepPostsADeeplyNestedSecret(string stepToken)
    {
        var body = $$"""
            {
              "order": {
                "id": 42,
                "customer": {
                  "name": "Ada",
                  "credentials": {
                    "username": "ada",
                    "password": "{{Secrets.NestedPassword}}",
                    "refresh_token": "{{Secrets.NestedToken}}"
                  }
                },
                "items": [ { "sku": "ABC", "apiKey": "{{Secrets.NestedApiKey}}" } ]
              }
            }
            """;

        var request = new HttpRequestMessage(HttpMethod.Post, $"/probe/{stepToken}")
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
        };

        (await client.Http.SendAsync(request)).EnsureSuccessStatusCode();
    }

    /// <summary>Asks the stub for a response bigger than the body cap, so truncation is
    /// exercised end to end rather than unit-tested in isolation.</summary>
    [Given(@"step (.*) receives a body larger than the cap")]
    public async Task StepReceivesAnOversizedBody(string stepToken)
    {
        (await client.Http.GetAsync($"/probe/{stepToken}?bulk=200000")).EnsureSuccessStatusCode();
    }

    [Then("the call should have completed")]
    public void ThenTheCallShouldHaveCompleted()
    {
        // The buffer drains at each AfterStep, so by here everything has been attached.
        _ = capture;
    }

    /// <summary>
    /// Screenshots default to on-failure-only, so proving capture works needs a failing step.
    /// The failure is the point, not an accident.
    /// </summary>
    [Then("the step fails on purpose")]
    public static void ThenTheStepFailsOnPurpose() =>
        throw new InvalidOperationException("Deliberate failure so an on-failure screenshot is taken.");

    [When("the scenario pauses briefly")]
    public static async Task PauseBriefly() => await Task.Delay(15);
}

/// <summary>
/// Secrets planted so a conformance test can prove they never reach the NDJSON. Real values,
/// not placeholders - a redaction test that greps for the word "redacted" proves nothing.
/// </summary>
public static class Secrets
{
    public const string BearerToken = "SPECREPORT-MUST-NOT-LEAK-BEARER-8f3a91";
    public const string ApiKey = "SPECREPORT-MUST-NOT-LEAK-APIKEY-77c2de";

    // Four levels deep, under keys that only a depth-aware redactor will reach.
    public const string NestedPassword = "SPECREPORT-MUST-NOT-LEAK-NESTEDPW-1a2b3c";
    public const string NestedToken = "SPECREPORT-MUST-NOT-LEAK-NESTEDTOKEN-4d5e6f";
    public const string NestedApiKey = "SPECREPORT-MUST-NOT-LEAK-NESTEDKEY-7g8h9i";

    /// <summary>Every planted secret, so a test can assert none of them reached the NDJSON.</summary>
    public static readonly string[] All =
        [BearerToken, ApiKey, NestedPassword, NestedToken, NestedApiKey];
}
