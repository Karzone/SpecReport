namespace SampleTests.Support;

/// <summary>
/// A deliberately tiny in-memory domain. The sample exists to produce a realistic Cucumber
/// Messages stream, so the domain only needs to be rich enough to pass, fail, and hold data.
/// </summary>
public sealed class Basket
{
    private readonly List<Product> _catalogue = [];
    private readonly List<Product> _lines = [];

    public bool CatalogueSeeded { get; private set; }
    public string? OrderNote { get; private set; }
    public IReadOnlyList<Product> Lines => _lines;

    public decimal Total => _lines.Sum(p => p.Price);

    public void SeedCatalogue() => CatalogueSeeded = true;

    public void AddToCatalogue(Product product) => _catalogue.Add(product);

    public Product? LastAdded => _catalogue.Count > 0 ? _catalogue[^1] : null;

    public void Add(Product product)
    {
        if (product.Stock > 0)
            _lines.Add(product);
    }

    public void AddAllInStock()
    {
        foreach (var p in _catalogue.Where(p => p.Stock > 0))
            _lines.Add(p);
    }

    public void ApplyDiscountBand(string band)
    {
        var factor = band switch
        {
            "none" or "keine" => 1.00m,
            "ten" or "zehn" => 0.90m,
            "half" => 0.50m,
            _ => 1.00m
        };

        for (var i = 0; i < _lines.Count; i++)
            _lines[i] = _lines[i] with { Price = decimal.Round(_lines[i].Price * factor, 2) };
    }

    public void SetOrderNote(string note) => OrderNote = note;
}

public sealed record Product(string Name, decimal Price, int Stock = 10);
