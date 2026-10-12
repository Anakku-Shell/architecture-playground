using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Shop.Modular.BuildingBlocks;
using Shop.Modular.BuildingBlocks.Infrastructure.Persistence;
using Shop.Modular.Ordering.Contracts;
using Shop.Modular.Payments.Contracts;
using Shop.Modular.Payments.Data;

namespace Shop.Modular.Payments.Features;

/// <summary>
/// COMMAND slice triggered by an event, not by HTTP: Ordering asks for a payment, this slice charges, records
/// the result and answers. Same file-per-use-case style as version 03. Guide: §7.2.
/// </summary>
internal sealed partial class ProcessPayment(
    PaymentsDbContext db,
    FakePaymentGateway gateway,
    IEventBus bus,
    TimeProvider time,
    ILogger<ProcessPayment> logger) : IIntegrationEventConsumer<PaymentRequested>
{
    public async Task ConsumeAsync(PaymentRequested integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        // Ordering locked the order before asking, so a second pay of the same order waits for this one and
        // is refused: the charge happens once. The order id still travels as the idempotency key, because a
        // real provider call can time out and be retried. The charge is NOT part of the database transaction:
        // if the commit fails after this line, the rows roll back but the money has moved (Guide §7.5, "Paying").
        var status = await gateway.ChargeAsync(integrationEvent.OrderId, integrationEvent.Amount, cancellationToken);
        var payment = new Payment
        {
            Id = Guid.CreateVersion7(),
            OrderId = integrationEvent.OrderId,
            Amount = integrationEvent.Amount,
            Status = status,
            ProcessedAt = time.GetUtcNow(),
        };
        db.Payments.Add(payment);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation())
        {
            throw new ConflictException($"Order {integrationEvent.OrderId} already has a payment.");
        }

        LogPaymentProcessed(logger, payment.OrderId, status);
        await (status == PaymentStatus.Approved
            ? bus.PublishAsync(new PaymentSucceeded(payment.OrderId, payment.Id), cancellationToken)
            : bus.PublishAsync(new PaymentDeclined(payment.OrderId, payment.Id), cancellationToken));
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Payment for order {OrderId}: {Status}")]
    private static partial void LogPaymentProcessed(ILogger logger, Guid orderId, PaymentStatus status);
}
