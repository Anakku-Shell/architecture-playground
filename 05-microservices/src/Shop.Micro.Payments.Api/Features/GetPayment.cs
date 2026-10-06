using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Shop.Micro.Payments.Api.Data;
using Shop.Micro.Payments.Api.Errors;

namespace Shop.Micro.Payments.Api.Features;

internal sealed record PaymentResponse(Guid Id, Guid OrderId, decimal Amount, PaymentStatus Status, DateTimeOffset ProcessedAt);

/// <summary>QUERY slice: GET /api/payments?orderId=…, a projection straight into the response.</summary>
internal static class GetPayment
{
    public static void Map(IEndpointRouteBuilder app) => app.MapGet("/api/payments", HandleAsync);

    private static async Task<Ok<PaymentResponse>> HandleAsync(Guid? orderId, PaymentsDbContext db, CancellationToken ct)
    {
        if (orderId is null || orderId == Guid.Empty)
        {
            throw new ValidationException("orderId", "The orderId query parameter is required.");
        }

        return TypedResults.Ok(await db.Payments.AsNoTracking()
            .Where(p => p.OrderId == orderId)
            .Select(p => new PaymentResponse(p.Id, p.OrderId, p.Amount, p.Status, p.ProcessedAt))
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException($"Order {orderId} has no payment."));
    }
}
