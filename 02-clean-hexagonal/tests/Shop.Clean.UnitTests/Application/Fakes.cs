using Shop.Clean.Application.Ports;
using Shop.Clean.Domain.Catalog;
using Shop.Clean.Domain.Common;
using Shop.Clean.Domain.Ordering;
using Shop.Clean.Domain.Payments;

namespace Shop.Clean.UnitTests.Application;

// Hand-written in-memory adapters for the ports. Because Application owns the interfaces, a test can plug
// these in instead of EF Core and PostgreSQL: dependency inversion is what makes the use cases testable
// in milliseconds. (No mocking library: a few small classes are easier to read.) Guide: §5.5.
//
// They behave like a real unit of work where it matters: a loaded product is a COPY, new aggregates are
// only pending, and nothing reaches the store until SaveChangesAsync. DiscardChanges drops it all, so a
// retried use case starts from the committed state, exactly as with EF Core. (Loaded orders are shared
// instances: the tests that need copies of orders are the contract tests, against PostgreSQL.)

/// <summary>The committed state: what a database would hold.</summary>
internal sealed class InMemoryStore
{
    public Dictionary<Guid, Product> Products { get; } = [];

    public Dictionary<Guid, Order> Orders { get; } = [];

    public List<Payment> Payments { get; } = [];

    public Dictionary<Guid, Product> TrackedProducts { get; } = [];

    public List<Order> PendingOrders { get; } = [];

    public List<Payment> PendingPayments { get; } = [];

    public void Seed(Product product) => Products[product.Id] = Copy(product);

    public Product Load(Product committed)
    {
        if (!TrackedProducts.TryGetValue(committed.Id, out var tracked))
        {
            TrackedProducts[committed.Id] = tracked = Copy(committed);
        }

        return tracked;
    }

    private static Product Copy(Product p) => Product.Create(p.Id, p.Name, p.Sku, p.Price, p.Stock);
}

internal sealed class InMemoryProducts(InMemoryStore store) : IProductRepository
{
    public Task<Product?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(store.Products.TryGetValue(id, out var p) ? store.Load(p) : null);

    public Task<IReadOnlyDictionary<Guid, Product>> GetManyAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<Guid, Product>>(
            store.Products.Values.Where(p => ids.Contains(p.Id)).Select(store.Load).ToDictionary(p => p.Id));

    public Task<IReadOnlyList<Product>> ListAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Product>>([.. store.Products.Values.OrderBy(p => p.Name.Value, StringComparer.Ordinal)]);

    public Task<bool> SkuExistsAsync(Sku sku, CancellationToken cancellationToken) =>
        Task.FromResult(store.Products.Values.Any(p => p.Sku == sku));

    public void Add(Product product) => store.TrackedProducts[product.Id] = product;
}

internal sealed class InMemoryOrders(InMemoryStore store) : IOrderRepository
{
    public Task<Order?> GetAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(store.Orders.GetValueOrDefault(id));

    public void Add(Order order) => store.PendingOrders.Add(order);
}

internal sealed class InMemoryPayments(InMemoryStore store) : IPaymentRepository
{
    public Task<Payment?> GetByOrderAsync(Guid orderId, CancellationToken cancellationToken) =>
        Task.FromResult(store.Payments.FirstOrDefault(p => p.OrderId == orderId));

    public void Add(Payment payment) => store.PendingPayments.Add(payment);
}

/// <summary>Commits tracked and pending changes on save; can be told to fail the next saves with a conflict.</summary>
internal sealed class FakeUnitOfWork(InMemoryStore store) : IUnitOfWork
{
    public int Saves { get; private set; }

    public int ConflictsToThrow { get; set; }

    public int Discards { get; private set; }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        if (ConflictsToThrow > 0)
        {
            ConflictsToThrow--;
            throw new ConcurrencyConflictException("Simulated conflict.");
        }

        foreach (var product in store.TrackedProducts.Values)
        {
            store.Products[product.Id] = product;
        }

        foreach (var order in store.PendingOrders)
        {
            store.Orders[order.Id] = order;
        }

        store.Payments.AddRange(store.PendingPayments);
        DiscardChanges();
        Discards--;
        Saves++;
        return Task.CompletedTask;
    }

    public void DiscardChanges()
    {
        store.TrackedProducts.Clear();
        store.PendingOrders.Clear();
        store.PendingPayments.Clear();
        Discards++;
    }
}

internal sealed class StubPaymentGateway(PaymentStatus answer) : IPaymentGateway
{
    public int Charges { get; private set; }

    public Task<PaymentStatus> ChargeAsync(Guid orderId, Money amount, CancellationToken cancellationToken)
    {
        Charges++;
        return Task.FromResult(answer);
    }
}

internal sealed class FixedTime : TimeProvider
{
    public static readonly DateTimeOffset Now = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => Now;
}
