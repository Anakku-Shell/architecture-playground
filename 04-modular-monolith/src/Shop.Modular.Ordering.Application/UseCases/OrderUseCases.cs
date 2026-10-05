using Microsoft.Extensions.Logging;
using Shop.Modular.BuildingBlocks;
using Shop.Modular.Ordering.Application.Common;
using Shop.Modular.Ordering.Application.IntegrationEvents;
using Shop.Modular.Ordering.Application.Ports;
using Shop.Modular.Ordering.Contracts;
using Shop.Modular.Ordering.Domain;

namespace Shop.Modular.Ordering.Application.UseCases;

public sealed class GetOrder(IOrderRepository orders)
{
    public async Task<Order> ExecuteAsync(Guid id, CancellationToken cancellationToken) =>
        await orders.GetAsync(id, cancellationToken) ?? throw OrderNotFound.For(id);
}

/// <summary>
/// Asks Payments to charge an order and returns the outcome. The order row is locked first, so two pays (or a
/// pay and a cancel) of the same order run one after the other: the second sees the first's result and is
/// refused before any money is requested. Versions 02 and 03 could not close that race (Guide §5.5). The charge itself stays outside the transaction (Guide §7.5).
/// </summary>
public sealed partial class PayOrder(IOrderRepository orders, IUnitOfWork unitOfWork, IEventBus bus, ILogger<PayOrder> logger)
{
    public async Task<Order> ExecuteAsync(Guid orderId, CancellationToken cancellationToken)
    {
        var order = await unitOfWork.InTransactionAsync(async ct =>
        {
            var current = await orders.GetForUpdateAsync(orderId, ct) ?? throw OrderNotFound.For(orderId);
            current.EnsureCanBePaid();

            // Payments charges and answers PaymentSucceeded or PaymentDeclined; this module consumes the answer
            // (and on a decline publishes OrderCancelled, so Catalog releases the stock), before this returns.
            await bus.PublishAsync(new PaymentRequested(current.Id, current.Total.Amount), ct);

            var after = await orders.GetAsync(orderId, ct);
            return after is { Status: not OrderStatus.AwaitingPayment }
                ? after
                : throw new InvalidOperationException($"Nobody answered {nameof(PaymentRequested)} for order {orderId}: is the Payments module registered?");
        }, cancellationToken);

        LogOrderPaymentRecorded(logger, order.Id, order.Status);
        return order;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Order {OrderId} payment recorded: {Status}")]
    private static partial void LogOrderPaymentRecorded(ILogger logger, Guid orderId, OrderStatus status);
}

/// <summary>The customer cancels an order awaiting payment; Catalog releases its stock in the same transaction.</summary>
public sealed partial class CancelOrder(IOrderRepository orders, IUnitOfWork unitOfWork, IEventBus bus, ILogger<CancelOrder> logger)
{
    public async Task<Order> ExecuteAsync(Guid orderId, CancellationToken cancellationToken)
    {
        var order = await unitOfWork.InTransactionAsync(async ct =>
        {
            var current = await orders.GetForUpdateAsync(orderId, ct) ?? throw OrderNotFound.For(orderId);
            current.Cancel();
            await unitOfWork.SaveChangesAsync(ct);
            await bus.PublishAsync(new OrderCancelled(current.Id, OrderItems.Of(current)), ct);
            return current;
        }, cancellationToken);

        LogOrderCancelled(logger, order.Id);
        return order;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Order {OrderId} cancelled by the customer")]
    private static partial void LogOrderCancelled(ILogger logger, Guid orderId);
}
