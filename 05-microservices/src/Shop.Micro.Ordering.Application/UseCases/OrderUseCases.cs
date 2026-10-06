using Microsoft.Extensions.Logging;
using Shop.Micro.Contracts.Catalog;
using Shop.Micro.Contracts.Payments;
using Shop.Micro.Ordering.Application.Common;
using Shop.Micro.Ordering.Application.Ports;
using Shop.Micro.Ordering.Domain;

namespace Shop.Micro.Ordering.Application.UseCases;

public sealed class GetOrder(IOrderRepository orders)
{
    public async Task<Order> ExecuteAsync(Guid id, CancellationToken cancellationToken) =>
        await orders.GetAsync(id, cancellationToken) ?? throw NotFoundException.Order(id);
}

/// <summary>
/// Asks Payments to charge an order and returns at once: the order is stored
/// <see cref="OrderStatus.PaymentPending"/> together with the <see cref="ProcessPayment"/> command, and the
/// client is told <c>202 Accepted</c>. A second pay, or a cancel, now finds PaymentPending and is refused,
/// so the charge is asked for once. If two requests read AwaitingPayment at the same moment, the second save
/// fails on the row version and becomes <c>409</c>. Guide: §8.6, "Paying".
/// </summary>
public sealed partial class PayOrder(IOrderRepository orders, IOutgoingMessages messages, IUnitOfWork unitOfWork, ILogger<PayOrder> logger)
{
    public async Task<Order> ExecuteAsync(Guid orderId, CancellationToken cancellationToken)
    {
        var order = await orders.GetAsync(orderId, cancellationToken) ?? throw NotFoundException.Order(orderId);
        order.RequestPayment();
        messages.Send(new ProcessPayment(order.Id, order.Total.Amount));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        LogPaymentRequested(logger, order.Id);
        return order;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Order {OrderId}: payment requested")]
    private static partial void LogPaymentRequested(ILogger logger, Guid orderId);
}

/// <summary>
/// The customer cancels an order awaiting payment. The order is cancelled at once (<c>200</c>); the stock comes
/// back a moment later, when Catalog handles <see cref="ReleaseStock"/>.
/// </summary>
public sealed partial class CancelOrder(IOrderRepository orders, IOutgoingMessages messages, IUnitOfWork unitOfWork, ILogger<CancelOrder> logger)
{
    public async Task<Order> ExecuteAsync(Guid orderId, CancellationToken cancellationToken)
    {
        var order = await orders.GetAsync(orderId, cancellationToken) ?? throw NotFoundException.Order(orderId);
        order.Cancel();
        messages.Send(new ReleaseStock(order.Id, OrderItems.Of(order)));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        LogOrderCancelled(logger, order.Id);
        return order;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Order {OrderId} cancelled by the customer; stock release requested")]
    private static partial void LogOrderCancelled(ILogger logger, Guid orderId);
}
