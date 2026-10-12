using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Shop.Slice.Api.Common;
using Shop.Slice.Api.Domain.Ordering;
using Shop.Slice.Api.Infrastructure.Persistence;

namespace Shop.Slice.Api.Features.Ordering.CancelOrder;

// COMMAND slice: POST /api/orders/{id}/cancel. The customer cancels; OrderFulfillment releases the stock.

internal sealed partial class CancelOrderEndpoint : IEndpoint
{
    public void Map(IEndpointRouteBuilder app) => app.MapPost("/api/orders/{id:guid}/cancel", HandleAsync);

    private static async Task<Ok<OrderResponse>> HandleAsync(Guid id, ShopDbContext db, ILogger<CancelOrderEndpoint> logger, CancellationToken ct)
    {
        var order = await db.RetryOnConflictAsync(async () =>
        {
            var current = await db.Orders.FirstOrDefaultAsync(o => o.Id == id, ct)
                ?? throw new NotFoundException($"Order {id} does not exist.");
            var productIds = current.Lines.Select(l => l.ProductId).ToList();
            var catalog = await db.Products.Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
            OrderFulfillment.Cancel(current, catalog);
            await db.SaveChangesAsync(ct);
            return current;
        });

        LogOrderCancelled(logger, order.Id);
        return TypedResults.Ok(OrderResponse.From(order));
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Order {OrderId} cancelled by the customer")]
    private static partial void LogOrderCancelled(ILogger logger, Guid orderId);
}
