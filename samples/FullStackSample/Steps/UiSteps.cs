using System.Text.Json;
using FullStackSample.Support;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Reqnroll;

namespace FullStackSample.Steps;

[Binding]
public sealed class UiSteps(BrowserSession browser, ApiState api)
{
    private IPage Page => browser.Page;

    [Given("the storefront is open")]
    [When("the storefront is open")]
    public async Task OpenStorefront()
    {
        await Page.GotoAsync(ShopHost.BaseUrl);

        // Wait for the catalogue to render rather than for a fixed delay - the page fetches it.
        await Page.WaitForSelectorAsync("#products li");
    }

    [When(@"I search for ""(.*)""")]
    public async Task Search(string term)
    {
        await Page.FillAsync("#search", term);

        // The filter is synchronous on input, but waiting for the DOM to settle keeps this
        // robust if it ever becomes async.
        await Page.WaitForFunctionAsync(
            "term => [...document.querySelectorAll('#products li .name')]" +
            ".every(n => n.textContent.toLowerCase().includes(term.toLowerCase()))", term);
    }

    [When(@"I add ""(.*)"" to the basket")]
    public async Task AddToBasket(string productName)
    {
        await Page.ClickAsync($"#products li:has(.name:text-is('{productName}')) button");
        await Page.WaitForFunctionAsync(
            "() => document.getElementById('basket-count').textContent !== '0'");
    }

    // ---- assertions --------------------------------------------------------------------

    [Then("I should see (.*) products? listed")]
    public async Task AssertProductCount(int expected)
    {
        var actual = await Page.Locator("#products li").CountAsync();

        Assert.AreEqual(expected, actual,
            $"The storefront listed {actual} product(s), not {expected}.");
    }

    [Then(@"the first product should be ""(.*)""")]
    public async Task AssertFirstProduct(string expected)
    {
        var actual = await Page.Locator("#products li .name").First.TextContentAsync();
        Assert.AreEqual(expected, actual);
    }

    [Then(@"the ""(.*)"" add button should be disabled")]
    public async Task AssertButtonDisabled(string sku)
    {
        var button = Page.Locator($"button[data-add='{sku}']");
        Assert.IsTrue(await button.IsDisabledAsync(), $"The add button for {sku} was enabled.");
    }

    [Then("the basket count should be (.*)")]
    public async Task AssertBasketCount(int expected)
    {
        var text = await Page.Locator("#basket-count").TextContentAsync();
        var actual = int.Parse(text ?? "0");

        Assert.AreEqual(expected, actual,
            $"The storefront basket showed {actual} item(s), not {expected}.");
    }

    [Then(@"the basket total should be ""(.*)""")]
    public async Task AssertBasketTotal(string expected)
    {
        var actual = await Page.Locator("#basket-total").TextContentAsync();
        Assert.AreEqual(expected, actual);
    }

    /// <summary>
    /// The cross-cutting assertion: what the browser shows against what the API returned. Both
    /// sides of it appear in the report, on the steps that produced them.
    /// </summary>
    [Then("the number of products on screen should match the API")]
    public async Task AssertUiMatchesApi()
    {
        using var json = JsonDocument.Parse(api.Body!);
        var fromApi = json.RootElement.GetArrayLength();
        var onScreen = await Page.Locator("#products li").CountAsync();

        Assert.AreEqual(fromApi, onScreen,
            $"The API returned {fromApi} products but the storefront rendered {onScreen}.");
    }
}
