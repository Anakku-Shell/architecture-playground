using Shop.Modular.BuildingBlocks;
using Shop.Modular.Catalog.Contracts;
using Shop.Modular.Ordering.Application.Ports;
using Shop.Modular.Ordering.Domain;

namespace Shop.Modular.UnitTests.Ordering.Application;

// In-memory stand-ins for what the Ordering application needs from the outside: its ports, the Catalog
// module (through its contract) and the event bus. They behave like the real ones where it matters: an
// added order is only readable after SaveChanges, and reading an order twice returns the same object (as
// EF Core does within one request).

internal sealed class FakeOrderRepository : IOrderRepository
{
    private readonly Dictionary<Guid, Order> _saved = [];
    private readonly List<Order> _added = [];

    public List<Guid> Locked { get; } = [];

    public Task<Order?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(_saved.GetValueOrDefault(id));

    public Task<Order?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken)
    {
        Locked.Add(id);
        return GetAsync(id, cancellationToken);
    }

    public void Add(Order order) => _added.Add(order);

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

internal sealed class FakeUnitOfWork(FakeOrderRepository orders) : IUnitOfWork
{
    public int Transactions { get; private set; }

    public int Saves { get; private set; }

    public async Task<T> InTransactionAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken)
    {
        Transactions++;
        return await work(cancellationToken);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        Saves++;
        orders.Commit();
        return Task.CompletedTask;
    }
}

internal sealed class FakeCatalog : ICatalogQueries
{
    private readonly Dictionary<Guid, ProductSnapshot> _products = [];

    public int Calls { get; private set; }

    public ProductSnapshot Add(string name = "Mug", decimal price = 10m)
    {
        var product = new ProductSnapshot(Guid.NewGuid(), name, price);
        _products[product.Id] = product;
        return product;
    }

    public Task<IReadOnlyList<ProductSnapshot>> GetProductsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        Calls++;
        return Task.FromResult<IReadOnlyList<ProductSnapshot>>([.. ids.Where(_products.ContainsKey).Select(id => _products[id])]);
    }
}

/// <summary>Records every published event and runs the reactions a test wires in (standing in for other modules).</summary>
internal sealed class RecordingEventBus : IEventBus
{
    private readonly Dictionary<Type, List<Func<object, Task>>> _reactions = [];

    public List<IIntegrationEvent> Published { get; } = [];

    public RecordingEventBus On<TEvent>(Func<TEvent, Task> reaction)
        where TEvent : IIntegrationEvent
    {
        if (!_reactions.TryGetValue(typeof(TEvent), out var list))
        {
            _reactions[typeof(TEvent)] = list = [];
        }

        list.Add(e => reaction((TEvent)e));
        return this;
    }

    public async Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken)
        where TEvent : IIntegrationEvent
    {
        Published.Add(integrationEvent);
        foreach (var reaction in _reactions.GetValueOrDefault(typeof(TEvent)) ?? [])
        {
            await reaction(integrationEvent);
        }
    }
}
