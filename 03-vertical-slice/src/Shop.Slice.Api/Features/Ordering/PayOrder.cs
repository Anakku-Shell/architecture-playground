using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Shop.Slice.Api.Common;
using Shop.Slice.Api.Domain.Catalog;
using Shop.Slice.Api.Domain.Common;
using Shop.Slice.Api.Domain.Ordering;
using Shop.Slice.Api.Domain.Payments;
using Shop.Slice.Api.Infrastructure;
using Shop.Slice.Api.Infrastructure.Persistence;

namespace Shop.Slice.Api.Features.Ordering.PayOrder;

// COMMAND slice: POST /api/orders/{id}/pay. Same flow and same trade-offs as version 02's PayOrder (charge
// once, outside the retry; idempotency key; the pay-versus-cancel window), written against EF Core directly.

internal sealed partial class PayOrderEndpoint : IEndpoint
{
    public void Map(IEndpointRouteBuilder app) => app.MapPost("/api/orders/{id:guid}/pay", HandleAsync);

    private static async Task<Ok<OrderResponse>> HandleAsync(
        Guid id,
        ShopDbContext db,
        FakePaymentGateway gateway,
        TimeProvider time,
        ILogger<PayOrderEndpoint> logger,
        CancellationToken ct)
    {
        // Check before charging: never send money for an order that cannot be paid.
        // Read-only (not tracked), so the first attempt below loads a fresh, tracked copy.
        var order = await db.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == id, ct) ?? throw NotFound(id);
        order.EnsureAwaitingPayment("paid");

        var status = await gateway.ChargeAsync(order.Id, order.Total, ct);

        var result = await db.RetryOnConflictAsync(async () =>
        {
            var current = await db.Orders.FirstOrDefaultAsync(o => o.Id == id, ct) ?? throw NotFound(id);
            var productIds = current.Lines.Select(l => l.ProductId).ToList();
            var catalog = status == PaymentStatus.Declined
                ? await db.Products.Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct)
                : new Dictionary<Guid, Product>();
            try
            {
                OrderFulfillment.RecordPayment(current, status, catalog);
            }
            catch (BusinessRuleViolationException) when (status == PaymentStatus.Approved)
            {
                // The money moved, but the order left AwaitingPayment meanwhile (a concurrent cancel or pay).
                LogChargedButNotRecorded(logger, id, current.Status);
                throw;
            }

            db.Payments.Add(Payment.Record(Guid.CreateVersion7(), current.Id, current.Total, status, time.GetUtcNow()));
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (ex is not DbUpdateConcurrencyException && ex.IsUniqueViolation())
            {
                throw new ConflictException($"Order {id} was paid by another request at the same time.");
            }

            return current;
        });

        LogOrderPaid(logger, result.Id, status);
        return TypedResults.Ok(OrderResponse.From(result));
    }

    private static NotFoundException NotFound(Guid id) => new($"Order {id} does not exist.");

    [LoggerMessage(Level = LogLevel.Information, Message = "Order {OrderId} payment {PaymentStatus}")]
    private static partial void LogOrderPaid(ILogger logger, Guid orderId, PaymentStatus paymentStatus);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Order {OrderId} was charged but became {Status} before the payment was recorded: refund required")]
    private static partial void LogChargedButNotRecorded(ILogger logger, Guid orderId, OrderStatus status);
}
