using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Shop.Slice.Api.Common;
using Shop.Slice.Api.Infrastructure.Persistence;

namespace Shop.Slice.Api.Features.Catalog.ListProducts;

/// <summary>
/// QUERY slice: GET /api/products. Reads straight from the database into the response, with no aggregate,
/// no change tracking and only the columns it shows: the read side of light CQRS (Guide §3.8, §6.4).
/// </summary>
internal sealed class ListProductsEndpoint : IEndpoint
{
    public void Map(IEndpointRouteBuilder app) => app.MapGet("/api/products", HandleAsync);

    private static async Task<Ok<List<ProductResponse>>> HandleAsync(ShopDbContext db, CancellationToken ct)
    {
        // EF Core turns each column into its value object (ProductName, Money); the last Select makes them plain values.
        var rows = await db.Products.AsNoTracking()
            .OrderBy(p => p.Name)
            .Select(p => new { p.Id, p.Name, p.Sku, p.Price, p.Stock })
            .ToListAsync(ct);

        return TypedResults.Ok(rows.Select(r => new ProductResponse(r.Id, r.Name.Value, r.Sku.Value, r.Price.Amount, r.Stock)).ToList());
    }
}
