using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Shop.Slice.Api.Common;
using Shop.Slice.Api.Infrastructure.Persistence;

namespace Shop.Slice.Api.Features.Ordering.GetOrder;

/// <summary>
/// QUERY slice: GET /api/orders/{id}. Projects the order and its lines (in request order) straight into the
/// response: no aggregate is built and nothing is tracked. Guide: §6.4.
/// </summary>
internal sealed class GetOrderEndpoint : IEndpoint
{
    public void Map(IEndpointRouteBuilder app) => app.MapGet("/api/orders/{id:guid}", HandleAsync);

    private static async Task<Ok<OrderResponse>> HandleAsync(Guid id, ShopDbContext db, CancellationToken ct)
    {
        var row = await db.Orders.AsNoTracking()
            .Where(o => o.Id == id)
            .Select(o => new
            {
                o.Id,
                o.CustomerId,
                o.Status,
                o.CancellationReason,
                o.Total,
                o.PlacedAt,
                Lines = o.Lines.OrderBy(l => l.LineNumber)
                    .Select(l => new { l.ProductId, l.ProductName, l.UnitPrice, l.Quantity, l.LineTotal })
                    .ToList(),
            })
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException($"Order {id} does not exist.");

        return TypedResults.Ok(new OrderResponse(
            row.Id,
            row.CustomerId,
            row.Status,
            row.CancellationReason,
            row.Total.Amount,
            [.. row.Lines.Select(l => new OrderLineResponse(l.ProductId, l.ProductName.Value, l.UnitPrice.Amount, l.Quantity.Value, l.LineTotal.Amount))],
            row.PlacedAt));
    }
}
