using Microsoft.Extensions.Logging;
using Shop.Modular.BuildingBlocks;
using Shop.Modular.Catalog.Contracts;
using Shop.Modular.Ordering.Application.Common;
using Shop.Modular.Ordering.Application.IntegrationEvents;
using Shop.Modular.Ordering.Application.Ports;
using Shop.Modular.Ordering.Contracts;
using Shop.Modular.Ordering.Domain;
using Shop.Modular.Ordering.Domain.Common;

namespace Shop.Modular.Ordering.Application.UseCases;

public sealed record PlaceOrderCommand(Guid CustomerId, IReadOnlyList<PlaceOrderLine>? Lines);

public sealed record PlaceOrderLine(Guid ProductId, int Quantity);

/// <summary>
/// The "place an order" use case. Compared with 02, the stock is no longer Ordering's business: the order is
/// stored <see cref="OrderStatus.Pending"/>, <see cref="OrderPlaced"/> is published, and Catalog answers
/// before <c>PublishAsync</c> returns, all inside one transaction. Follow it in Guide §7.5.
/// </summary>
public sealed partial class PlaceOrder(
    ICatalogQueries catalog,
    IOrderRepository orders,
    IUnitOfWork unitOfWork,
    IEventBus bus,
    TimeProvider time,
    ILogger<PlaceOrder> logger)
{
    public async Task<Order> ExecuteAsync(PlaceOrderCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var quantities = Validate(command);
        var lines = command.Lines!;

        var order = await unitOfWork.InTransactionAsync(async ct =>
        {
            // Names and prices come from Catalog's public query, never from its tables.
            var products = (await catalog.GetProductsAsync([.. lines.Select(l => l.ProductId)], ct)).ToDictionary(p => p.Id);
            var unknown = new ValidationErrors();
            for (var i = 0; i < lines.Count; i++)
            {
                if (!products.ContainsKey(lines[i].ProductId))
                {
                    unknown.Add($"lines[{i}].productId", $"Product {lines[i].ProductId} does not exist.");
                }
            }

            unknown.ThrowIfAny();

            // Of(), not Rehydrate(): another module's data is checked at the border like any other input.
            var placed = Order.Place(
                Guid.CreateVersion7(),
                command.CustomerId,
                [.. lines.Select((l, i) => OrderLine.Snapshot(l.ProductId, ProductName.Of(products[l.ProductId].Name), Money.Of(products[l.ProductId].Price), quantities[i]))],
                time.GetUtcNow());
            orders.Add(placed);
            await unitOfWork.SaveChangesAsync(ct);

            // Catalog reserves the stock (or not) and answers with an event that this module consumes
            // (StockReservedConsumer / StockReservationFailedConsumer), all before PublishAsync returns.
            await bus.PublishAsync(new OrderPlaced(placed.Id, OrderItems.Of(placed)), ct);

            var current = await orders.GetAsync(placed.Id, ct);
            return current is { Status: not OrderStatus.Pending }
                ? current
                : throw new InvalidOperationException($"Nobody answered {nameof(OrderPlaced)} for order {placed.Id}: is the Catalog module registered?");
        }, cancellationToken);

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
