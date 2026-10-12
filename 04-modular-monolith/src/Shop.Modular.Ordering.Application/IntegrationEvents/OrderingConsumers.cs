using Shop.Modular.BuildingBlocks;
using Shop.Modular.Catalog.Contracts;
using Shop.Modular.Ordering.Application.Common;
using Shop.Modular.Ordering.Application.Ports;
using Shop.Modular.Ordering.Contracts;
using Shop.Modular.Ordering.Domain;
using Shop.Modular.Payments.Contracts;

namespace Shop.Modular.Ordering.Application.IntegrationEvents;

// What the Ordering module does when another module answers it. Each consumer is a small use case whose
// trigger is an event instead of an HTTP request: load the order, let the aggregate decide, save. They run
// inside the transaction of the use case that published the question. Guide: §7.3.

/// <summary>Catalog reserved the stock: the order now awaits payment.</summary>
public sealed class StockReservedConsumer(IOrderRepository orders, IUnitOfWork unitOfWork) : IIntegrationEventConsumer<StockReserved>
{
    public async Task ConsumeAsync(StockReserved integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        var order = await orders.GetAsync(integrationEvent.OrderId, cancellationToken) ?? throw OrderNotFound.For(integrationEvent.OrderId);
        order.ConfirmStockReserved();
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>Catalog could not reserve the stock: the order is rejected (nothing was taken, nothing to give back).</summary>
public sealed class StockReservationFailedConsumer(IOrderRepository orders, IUnitOfWork unitOfWork) : IIntegrationEventConsumer<StockReservationFailed>
{
    public async Task ConsumeAsync(StockReservationFailed integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        var order = await orders.GetAsync(integrationEvent.OrderId, cancellationToken) ?? throw OrderNotFound.For(integrationEvent.OrderId);
        order.RejectForLackOfStock();
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>Payments charged the order.</summary>
public sealed class PaymentSucceededConsumer(IOrderRepository orders, IUnitOfWork unitOfWork) : IIntegrationEventConsumer<PaymentSucceeded>
{
    public async Task ConsumeAsync(PaymentSucceeded integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        var order = await orders.GetAsync(integrationEvent.OrderId, cancellationToken) ?? throw OrderNotFound.For(integrationEvent.OrderId);
        order.MarkPaid();
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>The payment was declined: the order is cancelled and its stock must come back, so Catalog is told.</summary>
public sealed class PaymentDeclinedConsumer(IOrderRepository orders, IUnitOfWork unitOfWork, IEventBus bus) : IIntegrationEventConsumer<PaymentDeclined>
{
    public async Task ConsumeAsync(PaymentDeclined integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        var order = await orders.GetAsync(integrationEvent.OrderId, cancellationToken) ?? throw OrderNotFound.For(integrationEvent.OrderId);
        order.DeclinePayment();
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await bus.PublishAsync(new OrderCancelled(order.Id, OrderItems.Of(order)), cancellationToken);
    }
}

/// <summary>An order's lines as the plain items other modules understand.</summary>
internal static class OrderItems
{
    public static IReadOnlyList<OrderedItem> Of(Order order) =>
        [.. order.Lines.Select(l => new OrderedItem(l.ProductId, l.Quantity.Value))];
}
