using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Shop.Slice.Api.Common;
using Shop.Slice.Api.Domain.Payments;
using Shop.Slice.Api.Infrastructure.Persistence;

namespace Shop.Slice.Api.Features.Payments.GetPayment;

// QUERY slice: GET /api/payments?orderId=… (the only payments slice, so its response lives here too).

public sealed record PaymentResponse(Guid Id, Guid OrderId, decimal Amount, PaymentStatus Status, DateTimeOffset ProcessedAt);

internal sealed class GetPaymentEndpoint : IEndpoint
{
    public void Map(IEndpointRouteBuilder app) => app.MapGet("/api/payments", HandleAsync);

    private static async Task<Ok<PaymentResponse>> HandleAsync(Guid? orderId, ShopDbContext db, CancellationToken ct)
    {
        if (orderId is null || orderId == Guid.Empty)
        {
            var errors = new ValidationErrors();
            errors.Add("orderId", "The orderId query parameter is required.");
            errors.ThrowIfAny();
        }

        var payment = await db.Payments.AsNoTracking()
            .Where(p => p.OrderId == orderId)
            .Select(p => new { p.Id, p.OrderId, p.Amount, p.Status, p.ProcessedAt })
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException($"Order {orderId} has no payment.");

        return TypedResults.Ok(new PaymentResponse(payment.Id, payment.OrderId, payment.Amount.Amount, payment.Status, payment.ProcessedAt));
    }
}
