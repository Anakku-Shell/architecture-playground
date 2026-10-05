using Microsoft.AspNetCore.Http.HttpResults;
using Shop.Slice.Api.Common;
using Shop.Slice.Api.Domain.Catalog;
using Shop.Slice.Api.Infrastructure.Persistence;

namespace Shop.Slice.Api.Features.Catalog.AdjustStock;

// COMMAND slice: POST /api/products/{id}/stock-adjustments. Product.AdjustStock decides; a result out of
// range comes back as a BusinessRuleViolationException (409).

public sealed record AdjustStockRequest(int Quantity);

internal sealed class AdjustStockEndpoint : IEndpoint
{
    public void Map(IEndpointRouteBuilder app) => app.MapPost("/api/products/{id:guid}/stock-adjustments", HandleAsync);

    private static async Task<Ok<ProductResponse>> HandleAsync(Guid id, AdjustStockRequest request, ShopDbContext db, CancellationToken ct)
    {
        var errors = new ValidationErrors();
        errors.Check("quantity", () => Product.EnsureValidAdjustment(request.Quantity));
        errors.ThrowIfAny();

        var product = await db.RetryOnConflictAsync(async () =>
        {
            var current = await db.Products.FindAsync([id], ct) ?? throw new NotFoundException($"Product {id} does not exist.");
            current.AdjustStock(request.Quantity);
            await db.SaveChangesAsync(ct);
            return current;
        });

        return TypedResults.Ok(ProductResponse.From(product));
    }
}
