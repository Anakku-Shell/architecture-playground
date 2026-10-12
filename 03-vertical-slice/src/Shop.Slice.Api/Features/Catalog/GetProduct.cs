using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Shop.Slice.Api.Common;
using Shop.Slice.Api.Infrastructure.Persistence;

namespace Shop.Slice.Api.Features.Catalog.GetProduct;

/// <summary>
/// QUERY slice: GET /api/products/{id}. The projection repeats ListProducts' on purpose: slices do not share
/// logic, so either can change (add a column, join another table) without touching the other. Guide: §6.6.
/// </summary>
internal sealed class GetProductEndpoint : IEndpoint
{
    public void Map(IEndpointRouteBuilder app) => app.MapGet("/api/products/{id:guid}", HandleAsync);

    private static async Task<Ok<ProductResponse>> HandleAsync(Guid id, ShopDbContext db, CancellationToken ct)
    {
        var row = await db.Products.AsNoTracking()
            .Where(p => p.Id == id)
            .Select(p => new { p.Id, p.Name, p.Sku, p.Price, p.Stock })
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException($"Product {id} does not exist.");

        return TypedResults.Ok(new ProductResponse(row.Id, row.Name.Value, row.Sku.Value, row.Price.Amount, row.Stock));
    }
}
