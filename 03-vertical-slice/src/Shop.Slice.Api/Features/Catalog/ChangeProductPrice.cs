using Microsoft.AspNetCore.Http.HttpResults;
using Shop.Slice.Api.Common;
using Shop.Slice.Api.Domain.Catalog;
using Shop.Slice.Api.Infrastructure.Persistence;

namespace Shop.Slice.Api.Features.Catalog.ChangeProductPrice;

// COMMAND slice: PUT /api/products/{id}/price.

public sealed record ChangePriceRequest(decimal Price);

internal sealed class ChangeProductPriceEndpoint : IEndpoint
{
    public void Map(IEndpointRouteBuilder app) => app.MapPut("/api/products/{id:guid}/price", HandleAsync);

    private static async Task<Ok<ProductResponse>> HandleAsync(Guid id, ChangePriceRequest request, ShopDbContext db, CancellationToken ct)
    {
        var errors = new ValidationErrors();
        var price = errors.Capture("price", () => Product.ValidPrice(request.Price));
        errors.ThrowIfAny();

        var product = await db.RetryOnConflictAsync(async () =>
        {
            var current = await db.Products.FindAsync([id], ct) ?? throw new NotFoundException($"Product {id} does not exist.");
            current.ChangePrice(price);
            await db.SaveChangesAsync(ct);
            return current;
        });

        return TypedResults.Ok(ProductResponse.From(product));
    }
}
