using Microsoft.Extensions.Logging.Abstractions;
using Shop.Modular.BuildingBlocks;
using Shop.Modular.Catalog.Contracts;
using Shop.Modular.Ordering.Application.IntegrationEvents;
using Shop.Modular.Ordering.Application.UseCases;
using Shop.Modular.Ordering.Contracts;
using Shop.Modular.Ordering.Domain;
using Shop.Modular.Ordering.Domain.Common;
using Shop.Modular.Payments.Contracts;
using Xunit;

namespace Shop.Modular.UnitTests.Ordering.Application;

// The Ordering module's use cases and consumers, with no database and no other module: Catalog and Payments
// are replaced by reactions on a recording bus, exactly what they are to Ordering (someone who answers an
// event). Guide: §7.5.
public sealed class PlaceOrderTests
{
    private readonly OrderingHarness _ordering = new();

    [Fact]
    public async Task WhenCatalogReservesStock_AwaitsPayment_AndPublishesOrderPlacedWithItsLines()
    {
        var mug = _ordering.Catalog.Add();
        _ordering.CatalogAnswers(reserved: true);

        var order = await _ordering.PlaceOrder.ExecuteAsync(Command((mug.Id, 2)), TestContext.Current.CancellationToken);

        Assert.Equal(OrderStatus.AwaitingPayment, order.Status);
        var placed = Assert.IsType<OrderPlaced>(_ordering.Bus.Published[0]);
        Assert.Equal(order.Id, placed.OrderId);
        Assert.Equal([new OrderedItem(mug.Id, 2)], placed.Items);
    }

    [Fact]
    public async Task WhenCatalogCannotReserve_IsRejected()
    {
        var mug = _ordering.Catalog.Add();
        _ordering.CatalogAnswers(reserved: false);

        var order = await _ordering.PlaceOrder.ExecuteAsync(Command((mug.Id, 2)), TestContext.Current.CancellationToken);

        Assert.Equal(OrderStatus.Rejected, order.Status);
    }

    [Fact]
    public async Task CopiesCatalogNameAndPrice_IntoTheLines()
    {
        var mug = _ordering.Catalog.Add("Blue mug", 12.50m);
        _ordering.CatalogAnswers(reserved: true);

        var order = await _ordering.PlaceOrder.ExecuteAsync(Command((mug.Id, 2)), TestContext.Current.CancellationToken);

        Assert.Equal("Blue mug", order.Lines[0].ProductName.Value);
        Assert.Equal(Money.Of(12.50m), order.Lines[0].UnitPrice);
        Assert.Equal(Money.Of(25m), order.Total);
    }

    [Fact]
    public async Task RunsInOneTransaction_AndSavesBeforePublishing()
    {
        var mug = _ordering.Catalog.Add();
        var savesWhenPublished = -1;
        _ordering.Bus.On<OrderPlaced>(_ =>
        {
            savesWhenPublished = _ordering.UnitOfWork.Saves;
            return Task.CompletedTask;
        });
        _ordering.CatalogAnswers(reserved: true);

        await _ordering.PlaceOrder.ExecuteAsync(Command((mug.Id, 1)), TestContext.Current.CancellationToken);

        Assert.Equal(1, _ordering.UnitOfWork.Transactions);
        Assert.Equal(1, savesWhenPublished);
    }

    [Fact]
    public async Task WithUnknownProduct_IsAValidationErrorOnThatLine_AndPublishesNothing()
    {
        var mug = _ordering.Catalog.Add();
        _ordering.CatalogAnswers(reserved: true);

        var error = await Assert.ThrowsAsync<ValidationException>(() =>
            _ordering.PlaceOrder.ExecuteAsync(Command((mug.Id, 1), (Guid.NewGuid(), 1)), TestContext.Current.CancellationToken));

        Assert.Equal(["lines[1].productId"], error.Errors.Keys);
        Assert.Empty(_ordering.Bus.Published);
    }

    [Fact]
    public async Task WithInvalidInput_ReportsEveryField_BeforeAskingCatalog()
    {
        var error = await Assert.ThrowsAsync<ValidationException>(() =>
            _ordering.PlaceOrder.ExecuteAsync(new PlaceOrderCommand(Guid.Empty, [new PlaceOrderLine(Guid.NewGuid(), 0)]), TestContext.Current.CancellationToken));

        Assert.Equal(["customerId", "lines[0].quantity"], error.Errors.Keys.Order());
        Assert.Equal(0, _ordering.Catalog.Calls);
    }

    [Fact]
    public async Task WhenCatalogDoesNotAnswer_Fails_InsteadOfLeavingTheOrderPending()
    {
        var mug = _ordering.Catalog.Add();

        await Assert.ThrowsAsync<InvalidOperationException>(() => _ordering.PlaceOrder.ExecuteAsync(Command((mug.Id, 1)), TestContext.Current.CancellationToken));
    }

    private static PlaceOrderCommand Command(params (Guid ProductId, int Quantity)[] lines) =>
        new(Guid.NewGuid(), [.. lines.Select(l => new PlaceOrderLine(l.ProductId, l.Quantity))]);
}

public sealed class PayAndCancelOrderTests
{
    private readonly OrderingHarness _ordering = new();

    [Fact]
    public async Task Pay_Approved_MarksPaid_AfterLockingTheOrder()
    {
        var order = _ordering.AwaitingPaymentOrder(total: 20m);
        _ordering.PaymentsAnswer(approved: true);

        var paid = await _ordering.PayOrder.ExecuteAsync(order.Id, TestContext.Current.CancellationToken);

        Assert.Equal(OrderStatus.Paid, paid.Status);
        Assert.Equal([order.Id], _ordering.Orders.Locked);
        Assert.Equal(new PaymentRequested(order.Id, 20m), _ordering.Bus.Published.OfType<PaymentRequested>().Single());
        Assert.Empty(_ordering.Bus.Published.OfType<OrderCancelled>());
    }

    [Fact]
    public async Task Pay_Declined_Cancels_AndPublishesOrderCancelledWithItsLines()
    {
        var order = _ordering.AwaitingPaymentOrder(total: 20m);
        _ordering.PaymentsAnswer(approved: false);

        var declined = await _ordering.PayOrder.ExecuteAsync(order.Id, TestContext.Current.CancellationToken);

        Assert.Equal(OrderStatus.Cancelled, declined.Status);
        Assert.Equal(CancellationReason.PaymentDeclined, declined.CancellationReason);
        var cancelled = _ordering.Bus.Published.OfType<OrderCancelled>().Single();
        Assert.Equal([new OrderedItem(order.Lines[0].ProductId, 2)], cancelled.Items);
    }

    [Fact]
    public async Task Pay_WhenNotAwaitingPayment_IsARuleViolation_AndAsksForNoMoney()
    {
        var order = _ordering.AwaitingPaymentOrder(total: 20m);
        _ordering.PaymentsAnswer(approved: true);
        await _ordering.PayOrder.ExecuteAsync(order.Id, TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<BusinessRuleViolationException>(() => _ordering.PayOrder.ExecuteAsync(order.Id, TestContext.Current.CancellationToken));
        Assert.Single(_ordering.Bus.Published.OfType<PaymentRequested>());
    }

    [Fact]
    public async Task Pay_UnknownOrder_IsNotFound() =>
        await Assert.ThrowsAsync<NotFoundException>(() => _ordering.PayOrder.ExecuteAsync(Guid.NewGuid(), TestContext.Current.CancellationToken));

    [Fact]
    public async Task Pay_WhenPaymentsDoesNotAnswer_Fails_InsteadOfReportingAwaitingPayment()
    {
        var order = _ordering.AwaitingPaymentOrder(total: 20m);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _ordering.PayOrder.ExecuteAsync(order.Id, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Cancel_Cancels_AndPublishesOrderCancelledWithItsLines()
    {
        var order = _ordering.AwaitingPaymentOrder(total: 20m);

        var cancelled = await _ordering.CancelOrder.ExecuteAsync(order.Id, TestContext.Current.CancellationToken);

        Assert.Equal(CancellationReason.CustomerCancelled, cancelled.CancellationReason);
        Assert.Equal([order.Id], _ordering.Orders.Locked);
        Assert.Equal([new OrderedItem(order.Lines[0].ProductId, 2)], _ordering.Bus.Published.OfType<OrderCancelled>().Single().Items);
    }

    [Fact]
    public async Task Cancel_PaidOrder_IsRefused_AndReleasesNothing()
    {
        var order = _ordering.AwaitingPaymentOrder(total: 20m);
        _ordering.PaymentsAnswer(approved: true);
        await _ordering.PayOrder.ExecuteAsync(order.Id, TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<BusinessRuleViolationException>(() => _ordering.CancelOrder.ExecuteAsync(order.Id, TestContext.Current.CancellationToken));
        Assert.Empty(_ordering.Bus.Published.OfType<OrderCancelled>());
    }

    [Fact]
    public async Task Get_Unknown_IsNotFound() =>
        await Assert.ThrowsAsync<NotFoundException>(() => _ordering.GetOrder.ExecuteAsync(Guid.NewGuid(), TestContext.Current.CancellationToken));
}

/// <summary>The Ordering module wired to fakes. Its own consumers are real; the other modules are reactions.</summary>
internal sealed class OrderingHarness
{
    public OrderingHarness()
    {
        UnitOfWork = new FakeUnitOfWork(Orders);
        Bus.On<StockReserved>(e => new StockReservedConsumer(Orders, UnitOfWork).ConsumeAsync(e, TestContext.Current.CancellationToken))
            .On<StockReservationFailed>(e => new StockReservationFailedConsumer(Orders, UnitOfWork).ConsumeAsync(e, TestContext.Current.CancellationToken))
            .On<PaymentSucceeded>(e => new PaymentSucceededConsumer(Orders, UnitOfWork).ConsumeAsync(e, TestContext.Current.CancellationToken))
            .On<PaymentDeclined>(e => new PaymentDeclinedConsumer(Orders, UnitOfWork, Bus).ConsumeAsync(e, TestContext.Current.CancellationToken));
    }

    public FakeOrderRepository Orders { get; } = new();

    public FakeUnitOfWork UnitOfWork { get; }

    public FakeCatalog Catalog { get; } = new();

    public RecordingEventBus Bus { get; } = new();

    public PlaceOrder PlaceOrder => new(Catalog, Orders, UnitOfWork, Bus, new FixedTime(), NullLogger<PlaceOrder>.Instance);

    public PayOrder PayOrder => new(Orders, UnitOfWork, Bus, NullLogger<PayOrder>.Instance);

    public CancelOrder CancelOrder => new(Orders, UnitOfWork, Bus, NullLogger<CancelOrder>.Instance);

    public GetOrder GetOrder => new(Orders);

    /// <summary>Stands in for Catalog: answers every OrderPlaced.</summary>
    public void CatalogAnswers(bool reserved) =>
        Bus.On<OrderPlaced>(e => reserved
            ? Bus.PublishAsync(new StockReserved(e.OrderId), TestContext.Current.CancellationToken)
            : Bus.PublishAsync(new StockReservationFailed(e.OrderId), TestContext.Current.CancellationToken));

    /// <summary>Stands in for Payments: answers every PaymentRequested.</summary>
    public void PaymentsAnswer(bool approved) =>
        Bus.On<PaymentRequested>(e => approved
            ? Bus.PublishAsync(new PaymentSucceeded(e.OrderId, Guid.NewGuid()), TestContext.Current.CancellationToken)
            : Bus.PublishAsync(new PaymentDeclined(e.OrderId, Guid.NewGuid()), TestContext.Current.CancellationToken));

    /// <summary>An order already placed and confirmed, with one line of 2 units.</summary>
    public Order AwaitingPaymentOrder(decimal total)
    {
        var order = Order.Place(Guid.NewGuid(), Guid.NewGuid(), [OrderLine.Snapshot(Guid.NewGuid(), ProductName.Of("Mug"), Money.Of(total / 2), Quantity.Of(2))], DateTimeOffset.UnixEpoch);
        order.ConfirmStockReserved();
        Orders.Seed(order);
        return order;
    }

    private sealed class FixedTime : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);
    }
}
