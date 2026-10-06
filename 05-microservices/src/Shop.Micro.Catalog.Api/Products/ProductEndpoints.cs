using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Shop.Micro.Catalog.Api.Data;
using Shop.Micro.Catalog.Api.Errors;

namespace Shop.Micro.Catalog.Api.Products;

internal sealed record CreateProductRequest(string? Name, string? Sku, decimal Price, int InitialStock);

internal sealed record ChangePriceRequest(decimal Price);

internal sealed record AdjustStockRequest(int Quantity);

internal sealed record ProductResponse(Guid Id, string Name, string Sku, decimal Price, int Stock)
{
    public static ProductResponse From(Product product) => new(product.Id, product.Name, product.Sku, product.Price, product.Stock);
}

/// <summary>
/// The Catalog service's HTTP API, CRUD style: each handler validates, uses the DbContext and answers. No
/// service, no repository, no domain model: three layers would only pass the same data along. Compare
/// with version 01, where this logic sat in a <c>ProductService</c> one layer down. Unchanged from 04: moving
/// Catalog into its own service did not change how it is written inside. Guide: §8.2.
/// </summary>
internal static partial class ProductEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var products = app.MapGroup("/api/products");
        products.MapPost("", CreateAsync);
        products.MapGet("", ListAsync);
        products.MapGet("/{id:guid}", GetAsync);
        products.MapPut("/{id:guid}/price", ChangePriceAsync);
        products.MapPost("/{id:guid}/stock-adjustments", AdjustStockAsync);
    }

    private static async Task<Created<ProductResponse>> CreateAsync(
        CreateProductRequest request, CatalogDbContext db, ILogger<Program> logger, CancellationToken ct)
    {
        var product = ProductRules.NewProduct(request.Name, request.Sku, request.Price, request.InitialStock);

        // Check first for a friendly message; the unique index catches two requests racing past the check.
        if (await db.Products.AnyAsync(p => p.Sku == product.Sku, ct))
        {
            throw DuplicateSku(product.Sku);
        }

        db.Products.Add(product);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation())
        {
            throw DuplicateSku(product.Sku);
        }

        LogProductCreated(logger, product.Id, product.Sku);
        return TypedResults.Created($"/api/products/{product.Id}", ProductResponse.From(product));
    }

    private static async Task<Ok<List<ProductResponse>>> ListAsync(CatalogDbContext db, CancellationToken ct) =>
        TypedResults.Ok(await db.Products.AsNoTracking()
            .OrderBy(p => p.Name)
            .Select(p => new ProductResponse(p.Id, p.Name, p.Sku, p.Price, p.Stock))
            .ToListAsync(ct));

    private static async Task<Ok<ProductResponse>> GetAsync(Guid id, CatalogDbContext db, CancellationToken ct) =>
        TypedResults.Ok(await db.Products.AsNoTracking()
            .Where(p => p.Id == id)
            .Select(p => new ProductResponse(p.Id, p.Name, p.Sku, p.Price, p.Stock))
            .FirstOrDefaultAsync(ct) ?? throw NotFound(id));

    private static async Task<Ok<ProductResponse>> ChangePriceAsync(Guid id, ChangePriceRequest request, CatalogDbContext db, CancellationToken ct)
    {
        ProductRules.CheckPrice(request.Price);
        var product = await db.Products.FindAsync([id], ct) ?? throw NotFound(id);
        product.Price = request.Price;

        // EF Core writes only the changed column (price), so a concurrent stock change is not overwritten.
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(ProductResponse.From(product));
    }

    private static async Task<Ok<ProductResponse>> AdjustStockAsync(Guid id, AdjustStockRequest request, CatalogDbContext db, CancellationToken ct)
    {
        ProductRules.CheckAdjustment(request.Quantity);

        // One conditional UPDATE, as in version 01: the database checks the range and changes the stock in
        // one atomic step, so a concurrent order cannot slip in between.
        var quantity = request.Quantity;
        var updated = await db.Products
            .Where(p => p.Id == id && p.Stock + quantity >= 0 && p.Stock + quantity <= ProductRules.MaxStock)
            .ExecuteUpdateAsync(set => set.SetProperty(p => p.Stock, p => p.Stock + quantity), ct);
        if (updated == 0)
        {
            throw await db.Products.AnyAsync(p => p.Id == id, ct)
                ? new ConflictException($"Stock of product {id} must stay between 0 and {ProductRules.MaxStock}.")
                : NotFound(id);
        }

        return await GetAsync(id, db, ct);
    }

    private static NotFoundException NotFound(Guid id) => new($"Product {id} does not exist.");

    private static ConflictException DuplicateSku(string sku) => new($"A product with SKU '{sku}' already exists.");

    [LoggerMessage(Level = LogLevel.Information, Message = "Product {ProductId} created with SKU {Sku}")]
    private static partial void LogProductCreated(ILogger logger, Guid productId, string sku);
}
