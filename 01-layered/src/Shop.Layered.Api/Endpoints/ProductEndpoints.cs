using Shop.Layered.Api.Models;
using Shop.Layered.Business.Catalog;

namespace Shop.Layered.Api.Endpoints;

/// <summary>
/// HTTP endpoints for the catalog. Each one only translates: request body → service call → response
/// record. No rule lives here; a rule found in an endpoint would be a layering mistake. Guide: §4.2.
/// </summary>
public static class ProductEndpoints
{
    public static IEndpointRouteBuilder MapProductEndpoints(this IEndpointRouteBuilder app)
    {
        var products = app.MapGroup("/api/products");

        products.MapPost("/", async (CreateProductRequest request, ProductService service, CancellationToken ct) =>
        {
            var product = await service.CreateAsync(request.Name, request.Sku, request.Price, request.InitialStock, ct);
            return TypedResults.Created($"/api/products/{product.Id}", ProductResponse.From(product));
        });

        products.MapGet("/", async (ProductService service, CancellationToken ct) =>
            TypedResults.Ok((await service.ListAsync(ct)).Select(ProductResponse.From)));

        products.MapGet("/{id:guid}", async (Guid id, ProductService service, CancellationToken ct) =>
            TypedResults.Ok(ProductResponse.From(await service.GetAsync(id, ct))));

        products.MapPut("/{id:guid}/price", async (Guid id, ChangePriceRequest request, ProductService service, CancellationToken ct) =>
            TypedResults.Ok(ProductResponse.From(await service.ChangePriceAsync(id, request.Price, ct))));

        products.MapPost("/{id:guid}/stock-adjustments", async (Guid id, AdjustStockRequest request, ProductService service, CancellationToken ct) =>
            TypedResults.Ok(ProductResponse.From(await service.AdjustStockAsync(id, request.Quantity, ct))));

        return app;
    }
}
