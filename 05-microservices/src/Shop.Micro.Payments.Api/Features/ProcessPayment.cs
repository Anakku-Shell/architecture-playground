using Microsoft.EntityFrameworkCore;
using Shop.Micro.Contracts;
using Shop.Micro.Contracts.Payments;
using Shop.Micro.Messaging;
using Shop.Micro.Payments.Api.Data;

namespace Shop.Micro.Payments.Api.Features;

/// <summary>
/// COMMAND slice triggered by a message: Ordering's saga asks for a charge, this slice charges, records the
/// result and answers through the outbox, all in the delivery's one local transaction. Same file-per-use-case
/// style as 03 and 04. Guide: §8.5.
/// </summary>
internal sealed partial class ProcessPaymentConsumer(
    PaymentsDbContext db,
    FakePaymentGateway gateway,
    IMessageOutbox outbox,
    TimeProvider time,
    ILogger<ProcessPaymentConsumer> logger) : IMessageConsumer<ProcessPayment>
{
    public async Task ConsumeAsync(ProcessPayment message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        // The inbox stops a redelivery of THIS message. A second ProcessPayment for the same order (a new
        // message id) is stopped here: answer again with the recorded outcome, never charge twice.
        var existing = await db.Payments.AsNoTracking().FirstOrDefaultAsync(p => p.OrderId == message.OrderId, cancellationToken);
        if (existing is not null)
        {
            LogAlreadyProcessed(logger, message.OrderId);
            outbox.Add(Answer(existing));
            return;
        }

        // The external call happens before the commit and is not part of it. If the commit then fails, the
        // message is redelivered and the charge asked for again: the order id travels as the provider's
        // idempotency key, so the provider recognises the repeat and does not charge twice (Guide §8.4).
        var status = await gateway.ChargeAsync(message.OrderId, message.Amount, cancellationToken);
        var payment = new Payment
        {
            Id = Guid.CreateVersion7(),
            OrderId = message.OrderId,
            Amount = message.Amount,
            Status = status,
            ProcessedAt = time.GetUtcNow(),
        };
        db.Payments.Add(payment);
        outbox.Add(Answer(payment));
        LogPaymentProcessed(logger, payment.OrderId, status);
    }

    private static IIntegrationMessage Answer(Payment payment) =>
        payment.Status == PaymentStatus.Approved
            ? new PaymentSucceeded(payment.OrderId, payment.Id)
            : new PaymentDeclined(payment.OrderId, payment.Id);

    [LoggerMessage(Level = LogLevel.Information, Message = "Payment for order {OrderId}: {Status}")]
    private static partial void LogPaymentProcessed(ILogger logger, Guid orderId, PaymentStatus status);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Order {OrderId} already has a payment; answering with its outcome without charging again")]
    private static partial void LogAlreadyProcessed(ILogger logger, Guid orderId);
}
