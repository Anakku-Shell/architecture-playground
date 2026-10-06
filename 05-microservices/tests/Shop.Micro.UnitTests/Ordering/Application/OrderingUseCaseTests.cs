using Microsoft.Extensions.Logging.Abstractions;
using Shop.Micro.Contracts.Catalog;
using Shop.Micro.Contracts.Payments;
using Shop.Micro.Ordering.Application.Common;
using Shop.Micro.Ordering.Application.Sagas;
using Shop.Micro.Ordering.Application.UseCases;
using Shop.Micro.Ordering.Domain;
using Shop.Micro.Ordering.Domain.Common;
using Xunit;

namespace Shop.Micro.UnitTests.Ordering.Application;

/// <summary>The Ordering service with every port faked: what a use case or the saga decides, and what it sends.</summary>
internal sealed class OrderingHarness
{
    public OrderingHarness()
    {
        UnitOfWork = new FakeUnitOfWork(Orders, Messages);
        PlaceOrder = new PlaceOrder(Catalog, Orders, Messages, UnitOfWork, TimeProvider.System, NullLogger<PlaceOrder>.Instance);
        PayOrder = new PayOrder(Orders, Messages, UnitOfWork, NullLogger<PayOrder>.Instance);
        CancelOrder = new CancelOrder(Orders, Messages, UnitOfWork, NullLogger<CancelOrder>.Instance);
        Saga = new OrderSaga(Orders, Messages, UnitOfWork, NullLogger<OrderSaga>.Instance);
    }

    public FakeCatalog Catalog { get; } = new();

    public FakeOrderRepository Orders { get; } = new();

    public FakeOutgoingMessages Messages { get; } = new();

    public FakeUnitOfWork UnitOfWork { get; }

    public PlaceOrder PlaceOrder { get; }

    public PayOrder PayOrder { get; }

    public CancelOrder CancelOrder { get; }

    public OrderSaga Saga { get; }

    public static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>An order already stored in <paramref name="status"/>, without any message sent.</summary>
    public Order Seed(OrderStatus status, decimal price = 10m, int quantity = 2)
    {
        var order = Order.Place(Guid.NewGuid(), Guid.NewGuid(), [OrderLine.Snapshot(Guid.NewGuid(), ProductName.Of("Mug"), Money.Of(price), Quantity.Of(quantity))], DateTimeOffset.UtcNow);
        if (status is not OrderStatus.Pending)
        {
            if (status is OrderStatus.Rejected)
            {
                order.RejectForLackOfStock();
            }
            else
            {
                order.ConfirmStockReserved();
            }
        }

        if (status is OrderStatus.PaymentPending or OrderStatus.Paid)
        {
            order.RequestPayment();
        }

        if (status is OrderStatus.Paid)
        {
            order.MarkPaid();
        }

        if (status is OrderStatus.Cancelled)
        {
            order.Cancel();
        }

        Orders.Seed(order);
        return order;
    }

    public static IReadOnlyList<OrderedItem> ItemsOf(Order order) => [.. order.Lines.Select(l => new OrderedItem(l.ProductId, l.Quantity.Value))];
}

public sealed class PlaceOrderTests
{
    private readonly OrderingHarness _ordering = new();

    [Fact]
    public async Task StoresAPendingOrder_AndAsksCatalogToReserveItsLines_InOneSave()
    {
        var mug = _ordering.Catalog.Add("Blue mug", 12.50m);

        var order = await _ordering.PlaceOrder.ExecuteAsync(Command((mug.Id, 2)), OrderingHarness.Ct);

        Assert.Equal(OrderStatus.Pending, order.Status);
        Assert.Same(order, _ordering.Orders.Saved(order.Id));
        Assert.Equal("Blue mug", order.Lines[0].ProductName.Value);
        Assert.Equal(Money.Of(25m), order.Total);
        var reserve = Assert.IsType<ReserveStock>(Assert.Single(_ordering.Messages.Sent));
        Assert.Equal(order.Id, reserve.OrderId);
        Assert.Equal([new OrderedItem(mug.Id, 2)], reserve.Items);
        Assert.Equal(1, _ordering.UnitOfWork.Saves);
    }

    [Fact]
    public async Task UnknownProduct_IsAValidationError_AndNothingIsSaved()
    {
        var error = await Assert.ThrowsAsync<ValidationException>(() =>
            _ordering.PlaceOrder.ExecuteAsync(Command((Guid.NewGuid(), 1)), OrderingHarness.Ct));

        Assert.Contains("lines[0].productId", error.Errors.Keys);
        Assert.Empty(_ordering.Messages.Sent);
        Assert.Equal(0, _ordering.UnitOfWork.Saves);
    }

    [Fact]
    public async Task InvalidInput_ListsEveryField_WithoutAskingCatalog()
    {
        _ordering.Catalog.Unavailable = true;

        var error = await Assert.ThrowsAsync<ValidationException>(() =>
            _ordering.PlaceOrder.ExecuteAsync(new PlaceOrderCommand(Guid.Empty, [new PlaceOrderLine(Guid.NewGuid(), 0)]), OrderingHarness.Ct));

        Assert.Contains("customerId", error.Errors.Keys);
        Assert.Contains("lines[0].quantity", error.Errors.Keys);
    }

    [Fact]
    public async Task CatalogUnavailable_Propagates_AndNothingIsSaved()
    {
        var mug = _ordering.Catalog.Add();
        _ordering.Catalog.Unavailable = true;

        await Assert.ThrowsAsync<CatalogUnavailableException>(() => _ordering.PlaceOrder.ExecuteAsync(Command((mug.Id, 1)), OrderingHarness.Ct));

        Assert.Equal(0, _ordering.UnitOfWork.Saves);
    }

    private static PlaceOrderCommand Command(params (Guid ProductId, int Quantity)[] lines) =>
        new(Guid.NewGuid(), [.. lines.Select(l => new PlaceOrderLine(l.ProductId, l.Quantity))]);
}

public sealed class PayOrderTests
{
    private readonly OrderingHarness _ordering = new();

    [Fact]
    public async Task AwaitingPayment_BecomesPaymentPending_AndAsksPaymentsToCharge()
    {
        var order = _ordering.Seed(OrderStatus.AwaitingPayment, price: 10m, quantity: 2);

        var paid = await _ordering.PayOrder.ExecuteAsync(order.Id, OrderingHarness.Ct);

        Assert.Equal(OrderStatus.PaymentPending, paid.Status);
        Assert.Equal(new ProcessPayment(order.Id, 20m), Assert.Single(_ordering.Messages.Sent));
    }

    [Theory]
    [InlineData(OrderStatus.Pending)]
    [InlineData(OrderStatus.PaymentPending)]
    [InlineData(OrderStatus.Paid)]
    [InlineData(OrderStatus.Rejected)]
    [InlineData(OrderStatus.Cancelled)]
    public async Task OutsideAwaitingPayment_IsRefused_AndNothingIsSent(OrderStatus status)
    {
        var order = _ordering.Seed(status);

        await Assert.ThrowsAsync<BusinessRuleViolationException>(() => _ordering.PayOrder.ExecuteAsync(order.Id, OrderingHarness.Ct));

        Assert.Empty(_ordering.Messages.Sent);
        Assert.Equal(status, order.Status);
    }

    [Fact]
    public async Task UnknownOrder_IsNotFound() =>
        await Assert.ThrowsAsync<NotFoundException>(() => _ordering.PayOrder.ExecuteAsync(Guid.NewGuid(), OrderingHarness.Ct));

    [Fact]
    public async Task AConcurrentChange_IsAConflict_AndNothingIsSent()
    {
        var order = _ordering.Seed(OrderStatus.AwaitingPayment);
        _ordering.UnitOfWork.ConflictOnNextSave = true;

        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => _ordering.PayOrder.ExecuteAsync(order.Id, OrderingHarness.Ct));

        Assert.Empty(_ordering.Messages.Sent);
    }
}

public sealed class CancelOrderTests
{
    private readonly OrderingHarness _ordering = new();

    [Fact]
    public async Task AwaitingPayment_IsCancelled_AndAsksCatalogToReleaseTheStock()
    {
        var order = _ordering.Seed(OrderStatus.AwaitingPayment);

        var cancelled = await _ordering.CancelOrder.ExecuteAsync(order.Id, OrderingHarness.Ct);

        Assert.Equal(OrderStatus.Cancelled, cancelled.Status);
        var release = Assert.IsType<ReleaseStock>(Assert.Single(_ordering.Messages.Sent));
        Assert.Equal(order.Id, release.OrderId);
        Assert.Equal(OrderingHarness.ItemsOf(order), release.Items);
    }

    [Theory]
    [InlineData(OrderStatus.Pending)]
    [InlineData(OrderStatus.PaymentPending)]
    [InlineData(OrderStatus.Paid)]
    [InlineData(OrderStatus.Rejected)]
    [InlineData(OrderStatus.Cancelled)]
    public async Task OutsideAwaitingPayment_IsRefused_AndNothingIsSent(OrderStatus status)
    {
        var order = _ordering.Seed(status);

        await Assert.ThrowsAsync<BusinessRuleViolationException>(() => _ordering.CancelOrder.ExecuteAsync(order.Id, OrderingHarness.Ct));

        Assert.Empty(_ordering.Messages.Sent);
    }
}
