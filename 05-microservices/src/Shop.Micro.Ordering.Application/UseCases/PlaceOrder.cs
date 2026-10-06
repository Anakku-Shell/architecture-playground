using Microsoft.Extensions.Logging;
using Shop.Micro.Contracts.Catalog;
using Shop.Micro.Ordering.Application.Common;
using Shop.Micro.Ordering.Application.Ports;
using Shop.Micro.Ordering.Domain;
using Shop.Micro.Ordering.Domain.Common;

namespace Shop.Micro.Ordering.Application.UseCases;

public sealed record PlaceOrderCommand(Guid CustomerId, IReadOnlyList<PlaceOrderLine>? Lines);

public sealed record PlaceOrderLine(Guid ProductId, int Quantity);

/// <summary>
/// The "place an order" use case, and the first step of the saga. It asks Catalog over HTTP for names and
/// prices (the customer waits for that answer), stores the order <see cref="OrderStatus.Pending"/> and sends
/// <see cref="ReserveStock"/> in the same save. It does NOT wait for the stock: the answer arrives later as a
/// message, and the client is told <c>202 Accepted</c>. Follow it in Guide §8.6.
/// </summary>
public sealed partial class PlaceOrder(
    ICatalogClient catalog,
    IOrderRepository orders,
    IOutgoingMessages messages,
    IUnitOfWork unitOfWork,
    TimeProvider time,
    ILogger<PlaceOrder> logger)
{
    public async Task<Order> ExecuteAsync(PlaceOrderCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var quantities = Validate(command);
        var lines = command.Lines!;

        // Synchronous question to another service: it can be slow or down (CatalogUnavailableException).
        var products = (await catalog.GetProductsAsync([.. lines.Select(l => l.ProductId)], cancellationToken)).ToDictionary(p => p.Id);
        var unknown = new ValidationErrors();
        for (var i = 0; i < lines.Count; i++)
        {
            if (!products.ContainsKey(lines[i].ProductId))
            {
                unknown.Add($"lines[{i}].productId", $"Product {lines[i].ProductId} does not exist.");
            }
        }

        unknown.ThrowIfAny();

        // Of(), not Rehydrate(): another service's data is checked at the border like any other input.
        var order = Order.Place(
            Guid.CreateVersion7(),
            command.CustomerId,
            [.. lines.Select((l, i) => OrderLine.Snapshot(l.ProductId, ProductName.Of(products[l.ProductId].Name), Money.Of(products[l.ProductId].Price), quantities[i]))],
            time.GetUtcNow());
        orders.Add(order);
        messages.Send(new ReserveStock(order.Id, OrderItems.Of(order)));

        // One save: the order and the outbox row commit together, or neither does (Guide §8.4).
        await unitOfWork.SaveChangesAsync(cancellationToken);
        LogOrderPlaced(logger, order.Id);
        return order;
    }

    /// <summary>Input validation, before asking anyone: is the request well formed?</summary>
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

    [LoggerMessage(Level = LogLevel.Information, Message = "Order {OrderId} placed: Pending, stock reservation requested")]
    private static partial void LogOrderPlaced(ILogger logger, Guid orderId);
}

/// <summary>An order's lines as the plain items Catalog understands.</summary>
internal static class OrderItems
{
    public static IReadOnlyList<OrderedItem> Of(Order order) =>
        [.. order.Lines.Select(l => new OrderedItem(l.ProductId, l.Quantity.Value))];
}
