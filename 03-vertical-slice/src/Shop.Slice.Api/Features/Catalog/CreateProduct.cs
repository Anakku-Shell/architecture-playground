using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Shop.Slice.Api.Common;
using Shop.Slice.Api.Domain.Catalog;
using Shop.Slice.Api.Domain.Common;
using Shop.Slice.Api.Infrastructure.Persistence;

namespace Shop.Slice.Api.Features.Catalog.CreateProduct;

// COMMAND slice: POST /api/products. Everything about this use case is in this file: the request, the
// validation, the handler and the route. Guide: §6.2.

public sealed record CreateProductRequest(string? Name, string? Sku, decimal Price, int InitialStock);

internal sealed partial class CreateProductEndpoint : IEndpoint
{
    public void Map(IEndpointRouteBuilder app) => app.MapPost("/api/products", HandleAsync);

    private static async Task<Created<ProductResponse>> HandleAsync(
        CreateProductRequest request,
        ShopDbContext db,
        ILogger<CreateProductEndpoint> logger,
        CancellationToken ct)
    {
        var errors = new ValidationErrors();
        var name = errors.Capture("name", () => ProductName.Of(request.Name));
        var sku = errors.Capture("sku", () => Sku.Of(request.Sku));
        var price = errors.Capture("price", () => Product.ValidPrice(request.Price));
        errors.ThrowIfAny();
        var product = errors.Capture("initialStock", () => Product.Create(Guid.CreateVersion7(), name, sku, price, request.InitialStock));
        errors.ThrowIfAny();

        // Check first for a friendly message; the unique index catches two requests racing past the check.
        if (await db.Products.AnyAsync(p => p.Sku == sku, ct))
        {
            throw DuplicateSku(sku);
        }

        db.Products.Add(product);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation())
        {
            throw DuplicateSku(sku);
        }

        LogProductCreated(logger, product.Id, product.Sku.Value);
        return TypedResults.Created($"/api/products/{product.Id}", ProductResponse.From(product));
    }

    private static ConflictException DuplicateSku(Sku sku) => new($"A product with SKU '{sku}' already exists.");

    [LoggerMessage(Level = LogLevel.Information, Message = "Product {ProductId} created with SKU {Sku}")]
    private static partial void LogProductCreated(ILogger logger, Guid productId, string sku);
}
