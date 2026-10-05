using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using Shop.Layered.Business.Catalog;
using Shop.Layered.Business.Errors;
using Shop.Layered.Business.Payments;
using Shop.Layered.Data;
using Shop.Layered.Data.Entities;

namespace Shop.Layered.Business.Ordering;

/// <summary>One line of a new order, as the Business layer receives it from the layer above.</summary>
public sealed record OrderLineInput(Guid ProductId, int Quantity);

/// <summary>
/// The order lifecycle: place, pay, cancel. Every rule about orders lives in this service (which
/// statuses can move to which, what gets snapshotted, when stock moves); the <see cref="Order"/> entity
/// is only data. To follow one request through this class, read Guide §4.5, "Journey of a request".
/// </summary>
public sealed partial class OrderService(
    ShopDbContext db,
    ProductService products,
    PaymentService payments,
    TimeProvider time,
    ILogger<OrderService> logger)
{
    private const int MaxQuantityPerLine = 1000;

    public async Task<Order> PlaceAsync(Guid customerId, IReadOnlyList<OrderLineInput>? requestedLines, CancellationToken cancellationToken)
    {
        var lines = Validate(customerId, requestedLines);

        // Names and prices come straight from the products table: in this version Ordering and Catalog
        // share one database and one DbContext. Read outside the transaction; the snapshot is what the
        // customer saw, and a price change afterwards does not touch this order.
        var ids = lines.Select(l => l.ProductId).ToList();
        var catalog = await db.Products.AsNoTracking()
            .Where(p => ids.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, cancellationToken);
        var unknown = lines
            .Select((line, index) => (line, index))
            .Where(x => !catalog.ContainsKey(x.line.ProductId))
            .ToDictionary(x => $"lines[{x.index}].productId", x => new[] { $"Product {x.line.ProductId} does not exist." });
        if (unknown.Count > 0)
        {
            throw new ValidationException(unknown);
        }

        var order = new Order
        {
            Id = Guid.CreateVersion7(),
            CustomerId = customerId,
            PlacedAt = time.GetUtcNow(),
            Lines = [.. lines.Select((l, index) => new OrderLine
            {
                Id = Guid.CreateVersion7(),
                LineNumber = index + 1,
                ProductId = l.ProductId,
                ProductName = catalog[l.ProductId].Name,
                UnitPrice = catalog[l.ProductId].Price,
                Quantity = l.Quantity,
                LineTotal = catalog[l.ProductId].Price * l.Quantity,
            })],
        };
        order.Total = order.Lines.Sum(l => l.LineTotal);

        // One database transaction: reserve every line and insert the order, or do neither.
        await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            if (await products.TryReserveStockAsync(StockLines(order), cancellationToken))
            {
                order.Status = OrderStatus.AwaitingPayment;
                db.Orders.Add(order);
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                LogOrderPlaced(logger, order.Id, order.Status);
                return order;
            }

            // Not enough stock for some line. Leaving the block without committing rolls the transaction
            // back, which also returns the units of the lines that were reserved before the failing one.
        }

        order.Status = OrderStatus.Rejected;
        db.Orders.Add(order);
        await db.SaveChangesAsync(cancellationToken);
        LogOrderPlaced(logger, order.Id, order.Status);
        return order;
    }

    public async Task<Order> GetAsync(Guid id, CancellationToken cancellationToken) =>
        await db.Orders.AsNoTracking().Include(o => o.Lines.OrderBy(l => l.LineNumber)).FirstOrDefaultAsync(o => o.Id == id, cancellationToken)
        ?? throw NotFound(id);

    public async Task<Order> PayAsync(Guid id, CancellationToken cancellationToken)
    {
        var order = await LoadForUpdateAsync(id, cancellationToken);
        EnsureAwaitingPayment(order, "paid");

        // The fake gateway answers in memory. A real one is a network call, and calling it inside a
        // database transaction would keep the transaction and its connection open for the whole call:
        // charge first, then store. The price of that order: two concurrent pays (or a pay racing a
        // cancel) both reach the gateway, and the loser's payment row is rolled back after money moved.
        // Real systems pass an idempotency key to the provider, refund the loser, or store a
        // "payment pending" state first (what version 05 does). Guide: §4.5, "The error path".
        var payment = payments.Charge(order);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (payment.Status == PaymentStatus.Approved)
        {
            order.Status = OrderStatus.Paid;
        }
        else
        {
            order.Status = OrderStatus.Cancelled;
            order.CancellationReason = CancellationReason.PaymentDeclined;
            await products.ReleaseStockAsync(StockLines(order), cancellationToken);
        }

        await SaveOrderAsync(order, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        LogOrderPaid(logger, order.Id, payment.Status);
        return order;
    }

    public async Task<Order> CancelAsync(Guid id, CancellationToken cancellationToken)
    {
        var order = await LoadForUpdateAsync(id, cancellationToken);
        EnsureAwaitingPayment(order, "cancelled");

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        order.Status = OrderStatus.Cancelled;
        order.CancellationReason = CancellationReason.CustomerCancelled;
        await products.ReleaseStockAsync(StockLines(order), cancellationToken);
        await SaveOrderAsync(order, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        LogOrderCancelled(logger, order.Id);
        return order;
    }

    /// <summary>Checks the input rules and returns the lines, known to be non-empty from here on.</summary>
    private static IReadOnlyList<OrderLineInput> Validate(Guid customerId, IReadOnlyList<OrderLineInput>? lines)
    {
        var errors = new Dictionary<string, string[]>();
        if (customerId == Guid.Empty)
        {
            errors["customerId"] = ["Customer id is required."];
        }

        if (lines is null || lines.Count == 0)
        {
            errors["lines"] = ["An order needs at least one line."];
        }
        else
        {
            for (var i = 0; i < lines.Count; i++)
            {
                if (lines[i].Quantity is < 1 or > MaxQuantityPerLine)
                {
                    errors[$"lines[{i}].quantity"] = [$"Quantity must be between 1 and {MaxQuantityPerLine}."];
                }
            }

            if (lines.Select(l => l.ProductId).Distinct().Count() != lines.Count)
            {
                errors["lines"] = ["Each product can appear only once in an order."];
            }
        }

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        return lines!;
    }

    private async Task<Order> LoadForUpdateAsync(Guid id, CancellationToken cancellationToken) =>
        await db.Orders.Include(o => o.Lines.OrderBy(l => l.LineNumber)).FirstOrDefaultAsync(o => o.Id == id, cancellationToken)
        ?? throw NotFound(id);

    private static void EnsureAwaitingPayment(Order order, string action)
    {
        if (order.Status != OrderStatus.AwaitingPayment)
        {
            throw new BusinessRuleException($"Order {order.Id} is {order.Status} and cannot be {action}.");
        }
    }

    /// <summary>
    /// Saves the order's new status. If another request changed the same order since we read it (its
    /// <c>xmin</c> moved) or already stored its payment (unique index), this request loses and gets a 409.
    /// </summary>
    private async Task SaveOrderAsync(Order order, CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex is DbUpdateConcurrencyException
            || ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new BusinessRuleException($"Order {order.Id} was changed by another request at the same time.");
        }
    }

    private static IEnumerable<(Guid ProductId, int Quantity)> StockLines(Order order) =>
        order.Lines.Select(l => (l.ProductId, l.Quantity));

    private static NotFoundException NotFound(Guid id) => new($"Order {id} does not exist.");

    [LoggerMessage(Level = LogLevel.Information, Message = "Order {OrderId} placed: {Status}")]
    private static partial void LogOrderPlaced(ILogger logger, Guid orderId, OrderStatus status);

    [LoggerMessage(Level = LogLevel.Information, Message = "Order {OrderId} payment {PaymentStatus}")]
    private static partial void LogOrderPaid(ILogger logger, Guid orderId, PaymentStatus paymentStatus);

    [LoggerMessage(Level = LogLevel.Information, Message = "Order {OrderId} cancelled by the customer")]
    private static partial void LogOrderCancelled(ILogger logger, Guid orderId);
}
