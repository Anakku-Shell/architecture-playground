using Microsoft.Extensions.Logging;
using Shop.Clean.Application.Common;
using Shop.Clean.Application.Ports;
using Shop.Clean.Domain.Common;
using Shop.Clean.Domain.Ordering;

namespace Shop.Clean.Application.UseCases.Ordering;

public sealed record PlaceOrderCommand(Guid CustomerId, IReadOnlyList<PlaceOrderLine>? Lines);

public sealed record PlaceOrderLine(Guid ProductId, int Quantity);

/// <summary>
/// The "place an order" use case. Follow it step by step in Guide §5.5, "Journey of a request".
/// </summary>
public sealed partial class PlaceOrder(
    IProductRepository products,
    IOrderRepository orders,
    IUnitOfWork unitOfWork,
    TimeProvider time,
    ILogger<PlaceOrder> logger)
{
    public async Task<Order> ExecuteAsync(PlaceOrderCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var quantities = Validate(command);
        var lines = command.Lines!;

        var order = await ConcurrencyRetry.ExecuteAsync(unitOfWork, async () =>
        {
            var catalog = await products.GetManyAsync([.. lines.Select(l => l.ProductId)], cancellationToken);
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
                command.CustomerId,
                [.. lines.Select((l, i) => (catalog[l.ProductId], quantities[i]))],
                time.GetUtcNow());
            orders.Add(placed);

            // One atomic save: the new order and every product whose stock changed. If another request
            // changed one of those products since they were loaded, this throws and the retry starts over.
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return placed;
        });

        LogOrderPlaced(logger, order.Id, order.Status);
        return order;
    }

    /// <summary>
    /// Input validation: is the request well formed? The domain checks the same invariants again (an Order
    /// cannot be built without lines), but only here do we know the JSON field names to report.
    /// </summary>
    private static List<Quantity> Validate(PlaceOrderCommand command)
    {
        var errors = new ValidationErrors();
        if (command.CustomerId == Guid.Empty)
        {
            errors.Add("customerId", "Customer id is required.");
        }

        var lines = command.Lines ?? [];
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
        return quantities;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Order {OrderId} placed: {Status}")]
    private static partial void LogOrderPlaced(ILogger logger, Guid orderId, OrderStatus status);
}
