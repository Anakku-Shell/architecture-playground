using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using Shop.Layered.Business.Errors;
using Shop.Layered.Data;
using Shop.Layered.Data.Entities;

namespace Shop.Layered.Business.Catalog;

/// <summary>
/// The catalog's business logic: validation, uniqueness, stock. A classic layered "service": it takes
/// the DbContext, reads and writes EF entities, and holds every rule about products (the entity has none).
/// Guide: §4.2.
/// </summary>
public sealed partial class ProductService(ShopDbContext db, ILogger<ProductService> logger)
{
    public async Task<Product> CreateAsync(string? name, string? sku, decimal price, int initialStock, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        var trimmedName = name?.Trim() ?? "";
        var normalizedSku = sku?.Trim().ToUpperInvariant() ?? "";
        if (trimmedName.Length is < 1 or > 200)
        {
            errors["name"] = ["Name must have between 1 and 200 characters."];
        }

        if (normalizedSku.Length is < 1 or > 50)
        {
            errors["sku"] = ["SKU must have between 1 and 50 characters."];
        }

        AddPriceErrors(price, errors);
        if (initialStock < 0)
        {
            errors["initialStock"] = ["Initial stock cannot be negative."];
        }

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        // Check first for a friendly message; the unique index below catches the race between two requests.
        if (await db.Products.AnyAsync(p => p.Sku == normalizedSku, cancellationToken))
        {
            throw DuplicateSku(normalizedSku);
        }

        var product = new Product { Id = Guid.CreateVersion7(), Name = trimmedName, Sku = normalizedSku, Price = price, Stock = initialStock };
        db.Products.Add(product);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw DuplicateSku(normalizedSku);
        }

        LogProductCreated(logger, product.Id, product.Sku);
        return product;
    }

    public async Task<IReadOnlyList<Product>> ListAsync(CancellationToken cancellationToken) =>
        await db.Products.AsNoTracking().OrderBy(p => p.Name).ToListAsync(cancellationToken);

    public async Task<Product> GetAsync(Guid id, CancellationToken cancellationToken) =>
        await db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, cancellationToken)
        ?? throw NotFound(id);

    public async Task<Product> ChangePriceAsync(Guid id, decimal price, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        AddPriceErrors(price, errors);
        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == id, cancellationToken) ?? throw NotFound(id);
        product.Price = price;
        await db.SaveChangesAsync(cancellationToken);
        return product;
    }

    /// <summary>Adds (positive) or removes (negative) units. The result can never go below zero.</summary>
    public async Task<Product> AdjustStockAsync(Guid id, int quantity, CancellationToken cancellationToken)
    {
        if (quantity == 0)
        {
            throw new ValidationException("quantity", "Quantity must not be zero.");
        }

        // One conditional UPDATE instead of read-check-write: the database applies the check and the
        // change atomically, so a concurrent order cannot slip in between. Guide: §4.5, step 7.
        var updated = await db.Products
            .Where(p => p.Id == id && p.Stock + quantity >= 0)
            .ExecuteUpdateAsync(set => set.SetProperty(p => p.Stock, p => p.Stock + quantity), cancellationToken);
        if (updated == 0)
        {
            var exists = await db.Products.AnyAsync(p => p.Id == id, cancellationToken);
            throw exists
                ? new BusinessRuleException($"Stock of product {id} cannot go below zero.")
                : NotFound(id);
        }

        return await GetAsync(id, cancellationToken);
    }

    /// <summary>
    /// Takes the units of every line, or none. Runs inside the caller's transaction (the one
    /// <see cref="Ordering.OrderService"/> opened on the same scoped DbContext): if a line fails, the caller
    /// rolls back and the lines already taken come back. Lines are processed in product id order so two
    /// multi-line orders always lock rows in the same order and cannot deadlock.
    /// </summary>
    public async Task<bool> TryReserveStockAsync(IEnumerable<(Guid ProductId, int Quantity)> lines, CancellationToken cancellationToken)
    {
        foreach (var (productId, quantity) in lines.OrderBy(l => l.ProductId))
        {
            var updated = await db.Products
                .Where(p => p.Id == productId && p.Stock >= quantity)
                .ExecuteUpdateAsync(set => set.SetProperty(p => p.Stock, p => p.Stock - quantity), cancellationToken);
            if (updated == 0)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Gives back the units of every line (an order was cancelled). Runs in the caller's transaction.</summary>
    public async Task ReleaseStockAsync(IEnumerable<(Guid ProductId, int Quantity)> lines, CancellationToken cancellationToken)
    {
        foreach (var (productId, quantity) in lines.OrderBy(l => l.ProductId))
        {
            await db.Products
                .Where(p => p.Id == productId)
                .ExecuteUpdateAsync(set => set.SetProperty(p => p.Stock, p => p.Stock + quantity), cancellationToken);
        }
    }

    private static void AddPriceErrors(decimal price, Dictionary<string, string[]> errors)
    {
        if (price <= 0)
        {
            errors["price"] = ["Price must be greater than zero."];
        }
        else if (!PriceRules.HasAtMostTwoDecimals(price))
        {
            errors["price"] = ["Price can have at most two decimals."];
        }
    }

    private static NotFoundException NotFound(Guid id) => new($"Product {id} does not exist.");

    private static BusinessRuleException DuplicateSku(string sku) => new($"A product with SKU '{sku}' already exists.");

    [LoggerMessage(Level = LogLevel.Information, Message = "Product {ProductId} created with SKU {Sku}")]
    private static partial void LogProductCreated(ILogger logger, Guid productId, string sku);
}
