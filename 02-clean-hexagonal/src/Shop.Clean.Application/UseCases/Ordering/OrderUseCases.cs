using Microsoft.Extensions.Logging;
using Shop.Clean.Application.Common;
using Shop.Clean.Application.Ports;
using Shop.Clean.Domain.Catalog;
using Shop.Clean.Domain.Common;
using Shop.Clean.Domain.Ordering;
using Shop.Clean.Domain.Payments;

namespace Shop.Clean.Application.UseCases.Ordering;

public sealed class GetOrder(IOrderRepository orders)
{
    public async Task<Order> ExecuteAsync(Guid id, CancellationToken cancellationToken) =>
        await orders.GetAsync(id, cancellationToken) ?? throw OrderNotFound.For(id);
}

/// <summary>Charges an order and records the result. A declined payment cancels the order and releases its stock.</summary>
public sealed partial class PayOrder(
    IOrderRepository orders,
    IProductRepository products,
    IPaymentRepository payments,
    IPaymentGateway gateway,
    IUnitOfWork unitOfWork,
    TimeProvider time,
    ILogger<PayOrder> logger)
{
    public async Task<Order> ExecuteAsync(Guid orderId, CancellationToken cancellationToken)
    {
        // Check before charging: never send money for an order that cannot be paid.
        var order = await orders.GetAsync(orderId, cancellationToken) ?? throw OrderNotFound.For(orderId);
        order.EnsureAwaitingPayment("paid");

        // Charge once, outside the retry: a retry must never charge again. The order id travels as the
        // provider's idempotency key, so two concurrent pays are charged once by a real provider. It does
        // not cover a cancel that commits between the charge and the save: see the warning below and
        // Guide §5.5, "The error path".
        var status = await gateway.ChargeAsync(order.Id, order.Total, cancellationToken);

        var result = await ConcurrencyRetry.ExecuteAsync(unitOfWork, async () =>
        {
            var current = await orders.GetAsync(orderId, cancellationToken) ?? throw OrderNotFound.For(orderId);
            var catalog = status == PaymentStatus.Declined
                ? await products.GetManyAsync([.. current.Lines.Select(l => l.ProductId)], cancellationToken)
                : new Dictionary<Guid, Product>();
            try
            {
                OrderFulfillment.RecordPayment(current, status, catalog);
            }
            catch (BusinessRuleViolationException) when (status == PaymentStatus.Approved)
            {
                // The money moved, but the order left AwaitingPayment meanwhile (a concurrent cancel or pay).
                // This version only makes it visible; version 05 avoids it with a PaymentPending state.
                LogChargedButNotRecorded(logger, orderId, current.Status);
                throw;
            }

            payments.Add(Payment.Record(Guid.CreateVersion7(), current.Id, current.Total, status, time.GetUtcNow()));
            try
            {
                await unitOfWork.SaveChangesAsync(cancellationToken);
            }
            catch (DuplicateKeyException)
            {
                throw new ConflictException($"Order {orderId} was paid by another request at the same time.");
            }

            return current;
        });

        LogOrderPaid(logger, result.Id, status);
        return result;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Order {OrderId} payment {PaymentStatus}")]
    private static partial void LogOrderPaid(ILogger logger, Guid orderId, PaymentStatus paymentStatus);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Order {OrderId} was charged but became {Status} before the payment was recorded: refund required")]
    private static partial void LogChargedButNotRecorded(ILogger logger, Guid orderId, OrderStatus status);
}

/// <summary>The customer cancels an order awaiting payment; its stock is released.</summary>
public sealed partial class CancelOrder(IOrderRepository orders, IProductRepository products, IUnitOfWork unitOfWork, ILogger<CancelOrder> logger)
{
    public async Task<Order> ExecuteAsync(Guid orderId, CancellationToken cancellationToken)
    {
        var order = await ConcurrencyRetry.ExecuteAsync(unitOfWork, async () =>
        {
            var current = await orders.GetAsync(orderId, cancellationToken) ?? throw OrderNotFound.For(orderId);
            var catalog = await products.GetManyAsync([.. current.Lines.Select(l => l.ProductId)], cancellationToken);
            OrderFulfillment.Cancel(current, catalog);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return current;
        });

        LogOrderCancelled(logger, order.Id);
        return order;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Order {OrderId} cancelled by the customer")]
    private static partial void LogOrderCancelled(ILogger logger, Guid orderId);
}

internal static class OrderNotFound
{
    public static NotFoundException For(Guid id) => new($"Order {id} does not exist.");
}
