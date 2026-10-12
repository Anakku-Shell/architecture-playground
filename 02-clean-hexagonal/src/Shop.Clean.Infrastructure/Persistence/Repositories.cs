using Microsoft.EntityFrameworkCore;
using Npgsql;
using Shop.Clean.Application.Ports;
using Shop.Clean.Domain.Catalog;
using Shop.Clean.Domain.Common;
using Shop.Clean.Domain.Ordering;
using Shop.Clean.Domain.Payments;

namespace Shop.Clean.Infrastructure.Persistence;

// The DRIVEN ADAPTERS for persistence: each class implements a port of Application with EF Core. They are
// internal; the rest of the system only ever sees the interfaces. Loaded aggregates are tracked by the
// shared, scoped ShopDbContext, so EfUnitOfWork saves whatever the use case changed. Guide: §5.2.

internal sealed class ProductRepository(ShopDbContext db) : IProductRepository
{
    public Task<Product?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        db.Products.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, Product>> GetManyAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
        await db.Products.Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, cancellationToken);

    public async Task<IReadOnlyList<Product>> ListAsync(CancellationToken cancellationToken) =>
        await db.Products.AsNoTracking().OrderBy(p => p.Name).ToListAsync(cancellationToken);

    public Task<bool> SkuExistsAsync(Sku sku, CancellationToken cancellationToken) =>
        db.Products.AnyAsync(p => p.Sku == sku, cancellationToken);

    public void Add(Product product) => db.Products.Add(product);
}

internal sealed class OrderRepository(ShopDbContext db) : IOrderRepository
{
    public Task<Order?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        db.Orders.FirstOrDefaultAsync(o => o.Id == id, cancellationToken);

    public void Add(Order order) => db.Orders.Add(order);
}

internal sealed class PaymentRepository(ShopDbContext db) : IPaymentRepository
{
    public Task<Payment?> GetByOrderAsync(Guid orderId, CancellationToken cancellationToken) =>
        db.Payments.AsNoTracking().FirstOrDefaultAsync(p => p.OrderId == orderId, cancellationToken);

    public void Add(Payment payment) => db.Payments.Add(payment);
}

/// <summary>
/// Implements <see cref="IUnitOfWork"/> with <c>SaveChanges</c>, which writes every tracked change in one
/// database transaction. It also translates EF Core and PostgreSQL errors into the exceptions the port
/// declares, so Application never sees a <c>DbUpdateException</c> or a <c>PostgresException</c>
/// (version 01's Business layer caught both).
/// </summary>
internal sealed class EfUnitOfWork(ShopDbContext db) : IUnitOfWork
{
    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConcurrencyConflictException(ex.Message);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } unique)
        {
            throw new DuplicateKeyException(unique.ConstraintName ?? unique.Message);
        }
    }

    public void DiscardChanges() => db.ChangeTracker.Clear();
}
