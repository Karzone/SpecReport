using System.Globalization;
using SampleTests.Support;
using Reqnroll;

namespace SampleTests.Steps;

[Binding]
public sealed class BasketSteps(Basket basket)
{
    // ---- Given -------------------------------------------------------------------------

    [Given("the catalogue is seeded")]
    [Given("der Katalog ist befüllt")]
    public void GivenTheCatalogueIsSeeded() => basket.SeedCatalogue();

    [Given("the session is anonymous")]
    public void GivenTheSessionIsAnonymous() { }

    [Given(@"a product ""(.*)"" priced (.*)")]
    [Given(@"ein Produkt ""(.*)"" kostet (.*)")]
    public void GivenAProductPriced(string name, decimal price)
        => basket.AddToCatalogue(new Product(name, price));

    [Given(@"a product ""(.*)"" priced (.*) with no stock")]
    public void GivenAProductPricedWithNoStock(string name, decimal price)
        => basket.AddToCatalogue(new Product(name, price, Stock: 0));

    [Given("the following products")]
    public void GivenTheFollowingProducts(DataTable table)
    {
        foreach (var row in table.Rows)
        {
            basket.AddToCatalogue(new Product(
                row["Name"],
                decimal.Parse(row["Price"], CultureInfo.InvariantCulture),
                int.Parse(row["Stock"], CultureInfo.InvariantCulture)));
        }
    }

    [Given("the following order note")]
    public void GivenTheFollowingOrderNote(string docString) => basket.SetOrderNote(docString);

    // ---- When --------------------------------------------------------------------------

    [Given("I add it to the basket")]
    [When("I add it to the basket")]
    [When("ich es in den Warenkorb lege")]
    public void WhenIAddItToTheBasket()
    {
        var product = basket.LastAdded
            ?? throw new InvalidOperationException("No product in the catalogue to add.");
        basket.Add(product);
    }

    [When("I add all in-stock products to the basket")]
    public void WhenIAddAllInStockProducts() => basket.AddAllInStock();

    [When(@"I apply the ""(.*)"" discount band")]
    [When(@"ich die Rabattstufe ""(.*)"" anwende")]
    public void WhenIApplyTheDiscountBand(string band)
    {
        if (basket.Lines.Count == 0 && basket.LastAdded is { } p)
            basket.Add(p);
        basket.ApplyDiscountBand(band);
    }

    [When("I submit the order note")]
    public void WhenISubmitTheOrderNote() { }

    [When("the pricing engine is rewritten")]
    public void WhenThePricingEngineIsRewritten()
        => throw new PendingStepException("Pricing engine rewrite is not done yet.");

    // ---- Then --------------------------------------------------------------------------

    [Then("the basket total should be (.*)")]
    [Then("sollte die Summe (.*) betragen")]
    public void ThenTheBasketTotalShouldBe(decimal expected)
    {
        if (basket.Total != expected)
        {
            throw new BasketAssertionException(
                $"Expected basket total to be {expected} but it was {basket.Total}.");
        }
    }

    [Then("the basket should contain (.*) item")]
    [Then("the basket should contain (.*) items")]
    public void ThenTheBasketShouldContainItems(int expected)
    {
        if (basket.Lines.Count != expected)
        {
            throw new BasketAssertionException(
                $"Expected {expected} item(s) in the basket but found {basket.Lines.Count}.");
        }
    }

    [Then("the checkout button should be enabled")]
    public void ThenTheCheckoutButtonShouldBeEnabled() { }

    [Then("the order note should be (.*) lines long")]
    public void ThenTheOrderNoteShouldBeLinesLong(int expected)
    {
        var actual = (basket.OrderNote ?? string.Empty)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;

        if (actual != expected)
            throw new BasketAssertionException($"Expected {expected} lines but found {actual}.");
    }
}

/// <summary>
/// A named exception type so the fixture proves `TestStepResult.exception.type` is populated
/// with something meaningful rather than a generic Exception.
/// </summary>
public sealed class BasketAssertionException(string message) : Exception(message);
