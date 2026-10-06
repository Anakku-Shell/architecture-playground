using Microsoft.Extensions.Logging;
using Shop.Micro.Contracts.Catalog;
using Shop.Micro.Contracts.Payments;
using Shop.Micro.Ordering.Application.Common;
using Shop.Micro.Ordering.Application.Ports;
using Shop.Micro.Ordering.Application.UseCases;
using Shop.Micro.Ordering.Domain;

namespace Shop.Micro.Ordering.Application.Sagas;

/// <summary>
/// The <b>orchestrated saga</b> of an order: the one place that knows the whole conversation between the
/// services. A saga replaces the single database transaction of 01-04 with a chain of local transactions,
/// one per service, linked by messages; when a later step fails, earlier steps are undone by a
/// <b>compensating</b> action instead of a rollback (here: <see cref="ReleaseStock"/> after a declined payment).
/// <para>
/// The saga's state is the order's status, so no extra table is needed. Each reply moves the order one step.
/// A reply that does not fit the current state (late, or a repeat the inbox could not catch because it has
/// a new message id) is logged and ignored: in a distributed system that is normal, not an error.
/// </para>
/// Guide: §8.5.
/// <code>
///   Pending ──StockReserved──────────▶ AwaitingPayment ──(pay)──▶ PaymentPending ──PaymentSucceeded──▶ Paid
///      └────StockReservationFailed──▶ Rejected                         └──────PaymentDeclined──▶ Cancelled + ReleaseStock
/// </code>
/// </summary>
public sealed partial class OrderSaga(IOrderRepository orders, IOutgoingMessages messages, IUnitOfWork unitOfWork, ILogger<OrderSaga> logger)
{
    public Task HandleAsync(StockReserved reply, CancellationToken cancellationToken) =>
        StepAsync(Id(reply?.OrderId), OrderStatus.Pending, nameof(StockReserved), order => order.ConfirmStockReserved(), cancellationToken);

    public Task HandleAsync(StockReservationFailed reply, CancellationToken cancellationToken) =>
        // Catalog took nothing: there is nothing to compensate.
        StepAsync(Id(reply?.OrderId), OrderStatus.Pending, nameof(StockReservationFailed), order => order.RejectForLackOfStock(), cancellationToken);

    public Task HandleAsync(PaymentSucceeded reply, CancellationToken cancellationToken) =>
        StepAsync(Id(reply?.OrderId), OrderStatus.PaymentPending, nameof(PaymentSucceeded), order => order.MarkPaid(), cancellationToken);

    public Task HandleAsync(PaymentDeclined reply, CancellationToken cancellationToken) =>
        StepAsync(Id(reply?.OrderId), OrderStatus.PaymentPending, nameof(PaymentDeclined), order =>
        {
            order.DeclinePayment();

            // Compensation: the stock was taken by an earlier, already committed step in another service.
            // It cannot be rolled back, so Catalog is asked to give it back.
            messages.Send(new ReleaseStock(order.Id, OrderItems.Of(order)));
        }, cancellationToken);

    private async Task StepAsync(Guid orderId, OrderStatus expected, string reply, Action<Order> step, CancellationToken cancellationToken)
    {
        // An unknown order is a real fault (a bug, or a message for another system): fail, so the message is
        // retried and then lands in the dead-letter queue where someone will look at it.
        var order = await orders.GetAsync(orderId, cancellationToken) ?? throw NotFoundException.Order(orderId);
        if (order.Status != expected)
        {
            LogReplyIgnored(logger, reply, order.Id, order.Status);
            return;
        }

        step(order);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        LogStep(logger, reply, order.Id, order.Status);
    }

    private static Guid Id(Guid? orderId) => orderId ?? throw new ArgumentNullException(nameof(orderId), "The reply is missing.");

    [LoggerMessage(Level = LogLevel.Information, Message = "Saga: {Reply} moved order {OrderId} to {Status}")]
    private static partial void LogStep(ILogger logger, string reply, Guid orderId, OrderStatus status);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Saga: ignored {Reply} for order {OrderId}, which is {Status}")]
    private static partial void LogReplyIgnored(ILogger logger, string reply, Guid orderId, OrderStatus status);
}
