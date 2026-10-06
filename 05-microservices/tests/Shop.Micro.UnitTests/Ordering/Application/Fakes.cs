using Shop.Micro.Contracts;
using Shop.Micro.Contracts.Catalog;
using Shop.Micro.Ordering.Application.Common;
using Shop.Micro.Ordering.Application.Ports;
using Shop.Micro.Ordering.Domain;

namespace Shop.Micro.UnitTests.Ordering.Application;

// In-memory stand-ins for the Ordering service's ports. They keep the one property the real adapters
// guarantee and the tests rely on: added orders and sent messages become real only at SaveChanges, together
// (the outbox). Guide: §8.5.

internal sealed class FakeOrderRepository : IOrderRepository
{
    private readonly Dictionary<Guid, Order> _saved = [];
    private readonly List<Order> _added = [];

    public Task<Order?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(_saved.GetValueOrDefault(id));

    public void Add(Order order) => _added.Add(order);

    public Order? Saved(Guid id) => _saved.GetValueOrDefault(id);

    public void Seed(Order order) => _saved[order.Id] = order;

    public void Commit()
    {
        foreach (var order in _added)
        {
            _saved[order.Id] = order;
        }

        _added.Clear();
    }
}

internal sealed class FakeOutgoingMessages : IOutgoingMessages
{
    private readonly List<IIntegrationMessage> _staged = [];

    /// <summary>Messages whose transaction committed: what the dispatcher would publish.</summary>
    public List<IIntegrationMessage> Sent { get; } = [];

    public void Send(IIntegrationMessage message) => _staged.Add(message);

    public void Commit()
    {
        Sent.AddRange(_staged);
        _staged.Clear();
    }
}

internal sealed class FakeUnitOfWork(FakeOrderRepository orders, FakeOutgoingMessages messages) : IUnitOfWork
{
    public int Saves { get; private set; }

    /// <summary>Makes the next save fail as if another request had changed the order first.</summary>
    public bool ConflictOnNextSave { get; set; }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        if (ConflictOnNextSave)
        {
            ConflictOnNextSave = false;
            throw new ConcurrencyConflictException("Changed by someone else.");
        }

        Saves++;
        orders.Commit();
        messages.Commit();
        return Task.CompletedTask;
    }
}

internal sealed class FakeCatalog : ICatalogClient
{
    private readonly Dictionary<Guid, ProductSnapshot> _products = [];

    public bool Unavailable { get; set; }

    public ProductSnapshot Add(string name = "Mug", decimal price = 10m)
    {
        var product = new ProductSnapshot(Guid.NewGuid(), name, price);
        _products[product.Id] = product;
        return product;
    }

    public Task<IReadOnlyList<ProductSnapshot>> GetProductsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
        Unavailable
            ? throw new CatalogUnavailableException("Catalog did not answer in time.", new TimeoutException())
            : Task.FromResult<IReadOnlyList<ProductSnapshot>>([.. ids.Where(_products.ContainsKey).Select(id => _products[id])]);
}
