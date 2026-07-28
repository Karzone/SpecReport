using System.Net;
using System.Text;
using System.Text.Json;
using FullStackSample.Support;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Reqnroll;
using RestSharp;

namespace FullStackSample.Steps;

[Binding]
public sealed class ApiSteps(ApiClients api, ApiState state)
{
    [When("I request the product catalogue")]
    public async Task RequestCatalogue()
    {
        state.Response = await api.Http.GetAsync("/api/products");
        state.Body = await state.Response.Content.ReadAsStringAsync();
    }

    [When(@"I request the product ""(.*)""")]
    public async Task RequestProduct(string sku)
    {
        state.Response = await api.Http.GetAsync($"/api/products/{sku}");
        state.Body = await state.Response.Content.ReadAsStringAsync();
    }

    /// <summary>
    /// The password goes in the request body and the tokens come back in the response. Neither
    /// should survive into the report - that is the point of this scenario.
    /// </summary>
    [When(@"I log in as ""(.*)""")]
    public async Task LogIn(string username)
    {
        // The credential lives in the step definition, not the feature file - see the comment
        // on the scenario. In a real suite this would come from configuration or a secret store.
        var payload = JsonSerializer.Serialize(
            new { username, password = "demo-password-not-in-the-gherkin" });

        state.Response = await api.Http.PostAsync(
            "/api/login", new StringContent(payload, Encoding.UTF8, "application/json"));

        state.Body = await state.Response.Content.ReadAsStringAsync();
    }

    [Given(@"I add ""(.*)"" to the basket via the API")]
    [When(@"I add ""(.*)"" to the basket via the API")]
    public async Task AddToBasket(string sku)
    {
        var payload = JsonSerializer.Serialize(new { sku, qty = 1 });

        state.Response = await api.Http.PostAsync(
            "/api/basket", new StringContent(payload, Encoding.UTF8, "application/json"));

        state.Body = await state.Response.Content.ReadAsStringAsync();
    }

    [Given("the basket is empty via the API")]
    public async Task EmptyBasket()
    {
        state.Response = await api.Http.DeleteAsync("/api/basket");
        state.Body = await state.Response.Content.ReadAsStringAsync();
    }

    [When("I request the product catalogue using RestSharp")]
    public async Task RequestCatalogueWithRestSharp()
    {
        // A different library entirely, captured identically - RestSharp sits on
        // HttpMessageHandler underneath, which is where SpecReport hooks in.
        state.RestResponse = await api.Rest.ExecuteAsync(new RestRequest("/api/products"));
    }

    // ---- assertions --------------------------------------------------------------------

    [Then("the response status should be (.*)")]
    public void AssertStatus(int expected) =>
        Assert.AreEqual((HttpStatusCode)expected, state.Response!.StatusCode,
            $"Expected HTTP {expected} from {state.Response.RequestMessage?.RequestUri}");

    [Then("the RestSharp response status should be (.*)")]
    public void AssertRestStatus(int expected) =>
        Assert.AreEqual((HttpStatusCode)expected, state.RestResponse!.StatusCode);

    [Then("the catalogue should contain (.*) products")]
    public void AssertCatalogueSize(int expected)
    {
        using var json = JsonDocument.Parse(state.Body!);
        var actual = json.RootElement.GetArrayLength();

        Assert.AreEqual(expected, actual,
            $"The catalogue returned {actual} products, not {expected}.");
    }

    [Then(@"the product name should be ""(.*)""")]
    public void AssertProductName(string expected)
    {
        using var json = JsonDocument.Parse(state.Body!);
        Assert.AreEqual(expected, json.RootElement.GetProperty("name").GetString());
    }

    [Then("the response should contain an access token")]
    public void AssertTokenPresent()
    {
        using var json = JsonDocument.Parse(state.Body!);

        // The test can see the token - it is a real response. The report must not.
        Assert.IsTrue(json.RootElement.TryGetProperty("access_token", out var token));
        Assert.IsFalse(string.IsNullOrWhiteSpace(token.GetString()));
    }
}

/// <summary>Per-scenario API state, shared between steps.</summary>
public sealed class ApiState
{
    public HttpResponseMessage? Response { get; set; }
    public string? Body { get; set; }
    public RestResponse? RestResponse { get; set; }
}
