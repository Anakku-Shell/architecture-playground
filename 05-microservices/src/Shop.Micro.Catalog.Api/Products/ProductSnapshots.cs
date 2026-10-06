using Microsoft.EntityFrameworkCore;
using Shop.Micro.Catalog.Api.Data;
using Shop.Micro.Contracts.Catalog;

namespace Shop.Micro.Catalog.Api.Products;

/// <summary>
/// The one question another service asks Catalog synchronously: names and prices of some products, for an
/// order being placed. Under <c>/internal</c>, which the gateway does not route, so it is not part of the
/// public API; its shape is <see cref="ProductSnapshot"/> in Contracts. Replaces 04's in-process
/// <c>ICatalogQueries</c>. Called as <c>?ids=…&amp;ids=…</c>. Guide: §8.6, step 3 of "Placing an order".
/// </summary>
internal static class ProductSnapshots
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/internal/product-snapshots", async (Guid[] ids, CatalogDbContext db, CancellationToken ct) =>
            TypedResults.Ok(await db.Products.AsNoTracking()
                .Where(p => ids.Contains(p.Id))
                .Select(p => new ProductSnapshot(p.Id, p.Name, p.Price))
                .ToListAsync(ct)));
}
