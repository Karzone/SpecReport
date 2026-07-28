using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FullStackSample.App;

/// <summary>
/// A tiny shop, hosted in-process, serving both an HTML page and a JSON API.
///
/// It exists so the sample is deterministic and offline: pointing UI tests at a real website
/// would make this suite flaky and slow, and the point is to show what the REPORT looks like
/// when one run contains both browser steps and API calls - not to test somebody else's site.
/// </summary>
public sealed class DemoShop : IAsyncDisposable
{
    private readonly WebApplication _app;

    /// <summary>
    /// Baskets are keyed by session, not global.
    ///
    /// Scenarios run in parallel and share this host - as they would share a real test
    /// environment - so a single global basket makes them race: one scenario's "add to basket"
    /// shows up in another's assertion. Both the API client and the browser send the same
    /// X-Session header per scenario, which is also how a real shop would work.
    /// </summary>
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, int>> _baskets = new();

    private ConcurrentDictionary<string, int> BasketFor(HttpRequest request)
    {
        var session = request.Headers.TryGetValue(SessionHeader, out var v) && v.Count > 0
            ? v[0] ?? "anon"
            : "anon";

        return _baskets.GetOrAdd(session, _ => new ConcurrentDictionary<string, int>());
    }

    public const string SessionHeader = "X-Session";

    /// <summary>Known only after the host starts, since the OS assigns the port.</summary>
    public string BaseUrl { get; private set; } = string.Empty;

    private static readonly Product[] Catalogue =
    [
        new("WIDGET-1", "Widget", 9.99m, 12),
        new("GADGET-2", "Gadget", 24.50m, 3),
        new("DOOHICK-3", "Doohickey", 4.25m, 0),
        new("GIZMO-4", "Gizmo", 15.00m, 7)
    ];

    private DemoShop(WebApplication app) => _app = app;

    public static async Task<DemoShop> StartAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();               // keep the test output readable
        builder.WebHost.UseUrls("http://127.0.0.1:0");  // let the OS pick a free port

        var shop = new DemoShop(builder.Build());
        shop.MapRoutes(shop._app);

        await shop._app.StartAsync();
        shop.BaseUrl = shop._app.Urls.First();

        return shop;
    }

    private void MapRoutes(WebApplication app)
    {
        app.MapGet("/", () => Results.Content(Html(), "text/html"));

        app.MapGet("/api/products", () => Results.Json(Catalogue));

        app.MapGet("/api/products/{sku}", (string sku) =>
        {
            var product = Catalogue.FirstOrDefault(p =>
                string.Equals(p.Sku, sku, StringComparison.OrdinalIgnoreCase));

            return product is null
                ? Results.Json(new { error = "not_found", sku }, statusCode: 404)
                : Results.Json(product);
        });

        // Returns a token, so the report can demonstrate redaction on a realistic response.
        app.MapPost("/api/login", async (HttpRequest request) =>
        {
            using var reader = new StreamReader(request.Body);
            var body = await reader.ReadToEndAsync();

            using var json = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
            var user = json.RootElement.TryGetProperty("username", out var u) ? u.GetString() : null;

            return Results.Json(new
            {
                user,
                access_token = "eyJhbGciOiJIUzI1NiJ9.DEMO-TOKEN-must-not-appear-in-the-report",
                refresh_token = "rt_DEMO-REFRESH-must-not-appear-in-the-report",
                expires_in = 3600
            });
        });

        app.MapPost("/api/basket", async (HttpRequest request) =>
        {
            using var reader = new StreamReader(request.Body);
            using var json = JsonDocument.Parse(await reader.ReadToEndAsync());

            var sku = json.RootElement.GetProperty("sku").GetString() ?? "";
            var qty = json.RootElement.TryGetProperty("qty", out var q) ? q.GetInt32() : 1;

            var product = Catalogue.FirstOrDefault(p => p.Sku == sku);
            if (product is null)
                return Results.Json(new { error = "unknown_sku", sku }, statusCode: 400);

            if (product.Stock < qty)
                return Results.Json(new { error = "out_of_stock", sku, available = product.Stock },
                                    statusCode: 409);

            BasketFor(request).AddOrUpdate(sku, qty, (_, existing) => existing + qty);
            return Results.Json(Basket(request));
        });

        app.MapGet("/api/basket", (HttpRequest request) => Results.Json(Basket(request)));

        app.MapDelete("/api/basket", (HttpRequest request) => { BasketFor(request).Clear(); return Results.Json(Basket(request)); });
    }

    private object Basket(HttpRequest request)
    {
        var lines = BasketFor(request).Select(kv =>
        {
            var product = Catalogue.First(p => p.Sku == kv.Key);
            return new { sku = kv.Key, name = product.Name, qty = kv.Value, price = product.Price };
        }).ToArray();

        return new { items = lines, count = lines.Sum(l => l.qty), total = lines.Sum(l => l.qty * l.price) };
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    private static string Html() =>
        """
        <!DOCTYPE html>
        <html lang="en">
        <head>
        <meta charset="utf-8">
        <title>Demo Shop</title>
        <style>
          body { font: 15px/1.5 -apple-system, "Segoe UI", Roboto, sans-serif;
                 margin: 0; padding: 32px; background: #f6f7fb; color: #14161c; }
          h1 { margin: 0 0 4px; font-size: 22px; }
          .sub { color: #666e7e; margin-bottom: 20px; font-size: 13.5px; }
          input { padding: 8px 11px; border: 1px solid #d8dce6; border-radius: 7px;
                  font: inherit; width: 260px; }
          ul { list-style: none; padding: 0; margin: 18px 0 0; display: grid; gap: 8px; max-width: 520px; }
          li { display: flex; align-items: center; gap: 12px; background: #fff;
               border: 1px solid #e2e5ec; border-radius: 9px; padding: 12px 14px; }
          .name { flex: 1; font-weight: 550; }
          .price { color: #666e7e; font-variant-numeric: tabular-nums; }
          button { background: #3b5bdb; color: #fff; border: 0; border-radius: 7px;
                   padding: 7px 13px; font: inherit; cursor: pointer; }
          button:disabled { background: #b9bfcc; cursor: not-allowed; }
          .basket { margin-top: 22px; padding: 13px 15px; background: #fff;
                    border: 1px solid #e2e5ec; border-radius: 9px; max-width: 520px; }
          .oos { color: #c92a3a; font-size: 12.5px; }
        </style>
        </head>
        <body>
          <h1>Demo Shop</h1>
          <div class="sub">A deterministic, offline shop used by the SpecReport full-stack sample.</div>

          <input id="search" placeholder="Filter products…" aria-label="Filter products">
          <ul id="products"></ul>

          <div class="basket">Basket: <b id="basket-count">0</b> item(s) — total <b id="basket-total">0.00</b></div>

        <script>
          let all = [];

          async function load() {
            all = await (await fetch('/api/products')).json();
            render();
            refreshBasket();
          }

          function render() {
            const q = document.getElementById('search').value.toLowerCase();
            const list = document.getElementById('products');
            list.innerHTML = '';

            all.filter(p => p.name.toLowerCase().includes(q)).forEach(p => {
              const li = document.createElement('li');
              li.setAttribute('data-sku', p.sku);

              const name = document.createElement('span');
              name.className = 'name';
              name.textContent = p.name;

              const price = document.createElement('span');
              price.className = 'price';
              price.textContent = p.price.toFixed(2);

              const btn = document.createElement('button');
              btn.textContent = p.stock > 0 ? 'Add to basket' : 'Out of stock';
              btn.disabled = p.stock === 0;
              btn.setAttribute('data-add', p.sku);
              btn.onclick = () => add(p.sku);

              li.append(name, price, btn);
              if (p.stock === 0) {
                const oos = document.createElement('span');
                oos.className = 'oos';
                oos.textContent = 'unavailable';
                li.append(oos);
              }
              list.append(li);
            });
          }

          async function add(sku) {
            await fetch('/api/basket', {
              method: 'POST',
              headers: { 'Content-Type': 'application/json' },
              body: JSON.stringify({ sku, qty: 1 })
            });
            refreshBasket();
          }

          async function refreshBasket() {
            const b = await (await fetch('/api/basket')).json();
            document.getElementById('basket-count').textContent = b.count;
            document.getElementById('basket-total').textContent = b.total.toFixed(2);
          }

          document.getElementById('search').addEventListener('input', render);
          load();
        </script>
        </body>
        </html>
        """;
}

public sealed record Product(string Sku, string Name, decimal Price, int Stock);

