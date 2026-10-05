using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Shop.Slice.Api.Common;
using Shop.Slice.Api.Domain.Common;
using Shop.Slice.Api.Domain.Ordering;
using Shop.Slice.Api.Infrastructure.Persistence;

namespace Shop.Slice.Api.Features.Ordering.PlaceOrder;

// COMMAND slice: POST /api/orders. The whole use case in one file, top to bottom: the request shape,
// input validation, loading, the domain decision, saving, the response. Follow it in Guide §6.5.

public sealed record PlaceOrderRequest(Guid CustomerId, IReadOnlyList<PlaceOrderLineRequest?>? Lines);

public sealed record PlaceOrderLineRequest(Guid ProductId, int Quantity);

internal sealed partial class PlaceOrderEndpoint : IEndpoint
{
    public void Map(IEndpointRouteBuilder app) => app.MapPost("/api/orders", HandleAsync);

    private static async Task<Created<OrderResponse>> HandleAsync(
        PlaceOrderRequest request,
        ShopDbContext db,
        TimeProvider time,
        ILogger<PlaceOrderEndpoint> logger,
        CancellationToken ct)
    {
        var (lines, quantities) = Validate(request);

        var order = await db.RetryOnConflictAsync(async () =>
        {
            var ids = lines.Select(l => l.ProductId).ToList();
            var catalog = await db.Products.Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
            var unknown = new ValidationErrors();
            for (var i = 0; i < lines.Count; i++)
            {
                if (!catalog.ContainsKey(lines[i].ProductId))
                {
                    unknown.Add($"lines[{i}].productId", $"Product {lines[i].ProductId} does not exist.");
                }
            }

            unknown.ThrowIfAny();

            // The domain decides: reserve every line or none, AwaitingPayment or Rejected.
            var placed = OrderFulfillment.Place(
                Guid.CreateVersion7(),
                request.CustomerId,
                [.. lines.Select((l, i) => (catalog[l.ProductId], quantities[i]))],
                time.GetUtcNow());
            db.Orders.Add(placed);

            // One SaveChanges = one transaction: the new order and every product whose stock changed. A
            // concurrent change to one of those products fails the xmin check and the attempt starts over.
            await db.SaveChangesAsync(ct);
            return placed;
        });

        LogOrderPlaced(logger, order.Id, order.Status);
        return TypedResults.Created($"/api/orders/{order.Id}", OrderResponse.From(order));
    }

    /// <summary>Input validation: is the request well formed? The domain checks its own invariants again.</summary>
    private static (List<PlaceOrderLineRequest> Lines, List<Quantity> Quantities) Validate(PlaceOrderRequest request)
    {
        var errors = new ValidationErrors();
        if (request.CustomerId == Guid.Empty)
        {
            errors.Add("customerId", "Customer id is required.");
        }

        // A null element ("lines": [null]) becomes an empty line, which fails below.
        var lines = request.Lines?.Select(l => l ?? new PlaceOrderLineRequest(Guid.Empty, 0)).ToList() ?? [];
        if (lines.Count == 0)
        {
            errors.Add("lines", "An order needs at least one line.");
        }

        var quantities = lines.Select((line, i) => errors.Capture($"lines[{i}].quantity", () => Quantity.Of(line.Quantity))).ToList();
        if (lines.Select(l => l.ProductId).Distinct().Count() != lines.Count)
        {
            errors.Add("lines", "Each product can appear only once in an order.");
        }

        errors.ThrowIfAny();
        return (lines, quantities);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Order {OrderId} placed: {Status}")]
    private static partial void LogOrderPlaced(ILogger logger, Guid orderId, OrderStatus status);
}
