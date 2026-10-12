using Shop.Clean.Domain.Catalog;
using Shop.Clean.Domain.Common;
using Shop.Clean.Domain.Ordering;
using Shop.Clean.Domain.Payments;

namespace Shop.Clean.Application.Ports;

// The PORTS of the hexagon (Guide §3.4): what the application needs from the outside world, written as
// interfaces in the application's own terms (aggregates, value objects). Infrastructure implements them
// (the driven adapters). This is dependency inversion: the call goes from the use case to the database,
// but the source dependency goes from Infrastructure to these interfaces. Guide: §5.2 and §5.4.

/// <summary>Loads and stores <see cref="Product"/> aggregates.</summary>
public interface IProductRepository
{
    Task<Product?> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>The products with these ids that exist, by id. Unknown ids are simply missing.</summary>
    Task<IReadOnlyDictionary<Guid, Product>> GetManyAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);

    /// <summary>Every product, ordered by name.</summary>
    Task<IReadOnlyList<Product>> ListAsync(CancellationToken cancellationToken);

    Task<bool> SkuExistsAsync(Sku sku, CancellationToken cancellationToken);

    void Add(Product product);
}

/// <summary>Loads and stores <see cref="Order"/> aggregates (with their lines).</summary>
public interface IOrderRepository
{
    Task<Order?> GetAsync(Guid id, CancellationToken cancellationToken);

    void Add(Order order);
}

/// <summary>Loads and stores <see cref="Payment"/>s.</summary>
public interface IPaymentRepository
{
    Task<Payment?> GetByOrderAsync(Guid orderId, CancellationToken cancellationToken);

    void Add(Payment payment);
}

/// <summary>Charges money through an external payment provider.</summary>
public interface IPaymentGateway
{
    /// <param name="orderId">Sent to the provider as an idempotency key: a repeated charge for the same order is ignored by a real provider.</param>
    /// <param name="amount">The amount to charge.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    Task<PaymentStatus> ChargeAsync(Guid orderId, Money amount, CancellationToken cancellationToken);
}

/// <summary>
/// Saves every change made to the loaded aggregates in one atomic step (a <b>unit of work</b>). The
/// repositories only collect changes; nothing is written until <see cref="SaveChangesAsync"/>.
/// </summary>
public interface IUnitOfWork
{
    /// <exception cref="ConcurrencyConflictException">Another request changed one of the aggregates since it was loaded.</exception>
    /// <exception cref="DuplicateKeyException">A unique value (a SKU, the payment of an order) already exists.</exception>
    Task SaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>Forgets every loaded aggregate and pending change, so a retry starts from fresh data.</summary>
    void DiscardChanges();
}

/// <summary>Part of the <see cref="IUnitOfWork"/> contract: an optimistic concurrency check failed.</summary>
public sealed class ConcurrencyConflictException(string message) : Exception(message);

/// <summary>Part of the <see cref="IUnitOfWork"/> contract: a unique value already exists.</summary>
public sealed class DuplicateKeyException(string message) : Exception(message);
