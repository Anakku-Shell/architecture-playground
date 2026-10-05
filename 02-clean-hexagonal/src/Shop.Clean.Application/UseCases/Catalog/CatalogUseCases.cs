using Microsoft.Extensions.Logging;
using Shop.Clean.Application.Common;
using Shop.Clean.Application.Ports;
using Shop.Clean.Domain.Catalog;
using Shop.Clean.Domain.Common;

namespace Shop.Clean.Application.UseCases.Catalog;

// One class per use case (operation). Each one does the same four things: validate the input, load the
// aggregates through ports, ask the domain to decide, save through the unit of work. No rule lives here
// that the domain could hold. Guide: §5.2.

public sealed record CreateProductCommand(string? Name, string? Sku, decimal Price, int InitialStock);

public sealed partial class CreateProduct(IProductRepository products, IUnitOfWork unitOfWork, ILogger<CreateProduct> logger)
{
    public async Task<Product> ExecuteAsync(CreateProductCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var errors = new ValidationErrors();
        var name = errors.Capture("name", () => ProductName.Of(command.Name));
        var sku = errors.Capture("sku", () => Sku.Of(command.Sku));
        var price = errors.Capture("price", () => Money.Of(command.Price));
        errors.ThrowIfAny();
        var product = errors.Capture("initialStock", () => Product.Create(Guid.CreateVersion7(), name, sku, price, command.InitialStock));
        errors.ThrowIfAny();

        // Check first for a friendly message; the unique index behind the port catches the race.
        if (await products.SkuExistsAsync(sku, cancellationToken))
        {
            throw DuplicateSku(sku);
        }

        products.Add(product);
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DuplicateKeyException)
        {
            throw DuplicateSku(sku);
        }

        LogProductCreated(logger, product.Id, product.Sku.Value);
        return product;
    }

    private static ConflictException DuplicateSku(Sku sku) => new($"A product with SKU '{sku}' already exists.");

    [LoggerMessage(Level = LogLevel.Information, Message = "Product {ProductId} created with SKU {Sku}")]
    private static partial void LogProductCreated(ILogger logger, Guid productId, string sku);
}

public sealed class ListProducts(IProductRepository products)
{
    public Task<IReadOnlyList<Product>> ExecuteAsync(CancellationToken cancellationToken) => products.ListAsync(cancellationToken);
}

public sealed class GetProduct(IProductRepository products)
{
    public async Task<Product> ExecuteAsync(Guid id, CancellationToken cancellationToken) =>
        await products.GetAsync(id, cancellationToken) ?? throw ProductNotFound.For(id);
}

public sealed record ChangeProductPriceCommand(Guid ProductId, decimal Price);

public sealed class ChangeProductPrice(IProductRepository products, IUnitOfWork unitOfWork)
{
    public async Task<Product> ExecuteAsync(ChangeProductPriceCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var errors = new ValidationErrors();
        var price = errors.Capture("price", () => Money.Of(command.Price));
        errors.ThrowIfAny();

        return await ConcurrencyRetry.ExecuteAsync(unitOfWork, async () =>
        {
            var product = await products.GetAsync(command.ProductId, cancellationToken) ?? throw ProductNotFound.For(command.ProductId);
            product.ChangePrice(price);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return product;
        });
    }
}

public sealed record AdjustStockCommand(Guid ProductId, int Quantity);

public sealed class AdjustStock(IProductRepository products, IUnitOfWork unitOfWork)
{
    public async Task<Product> ExecuteAsync(AdjustStockCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.Quantity == 0)
        {
            var errors = new ValidationErrors();
            errors.Add("quantity", "Quantity must not be zero.");
            errors.ThrowIfAny();
        }

        // Product.AdjustStock decides; "below zero" comes back as a BusinessRuleViolationException (409).
        return await ConcurrencyRetry.ExecuteAsync(unitOfWork, async () =>
        {
            var product = await products.GetAsync(command.ProductId, cancellationToken) ?? throw ProductNotFound.For(command.ProductId);
            product.AdjustStock(command.Quantity);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return product;
        });
    }
}

internal static class ProductNotFound
{
    public static NotFoundException For(Guid id) => new($"Product {id} does not exist.");
}
