using Shop.Modular.Ordering.Domain.Common;

namespace Shop.Modular.Ordering.Domain;

/// <summary>Where an order is in its lifecycle (Guide §3.11).</summary>
public enum OrderStatus
{
    /// <summary>Placed, waiting for Catalog to reserve its stock. In 04 it never leaves the request that placed it.</summary>
    Pending,
    AwaitingPayment,
    Rejected,
    Paid,
    Cancelled,
}

/// <summary>Why an order was cancelled.</summary>
public enum CancellationReason
{
    CustomerCancelled,
    PaymentDeclined,
}

/// <summary>
/// An order and its lines: the <b>aggregate root</b> of the Ordering module, as in version 02. What changed:
/// the order no longer touches products. In 02 a domain service took stock from <c>Product</c> objects;
/// here Product belongs to another module, so the order starts <see cref="OrderStatus.Pending"/> and is told
/// the outcome (<see cref="ConfirmStockReserved"/> or <see cref="RejectForLackOfStock"/>). Guide: §7.2.
/// </summary>
public sealed class Order
{
    private readonly List<OrderLine> _lines = [];

    private Order()
    {
        Total = null!;
    }

    private Order(Guid id, Guid customerId, IReadOnlyList<OrderLine> lines, DateTimeOffset placedAt)
    {
        if (customerId == Guid.Empty)
        {
            throw new DomainValidationException("Customer id is required.");
        }

        if (lines.Count == 0)
        {
            throw new DomainValidationException("An order needs at least one line.");
        }

        if (lines.Select(l => l.ProductId).Distinct().Count() != lines.Count)
        {
            throw new DomainValidationException("Each product can appear only once in an order.");
        }

        Id = id;
        CustomerId = customerId;
        Status = OrderStatus.Pending;
        PlacedAt = placedAt;
        for (var i = 0; i < lines.Count; i++)
        {
            lines[i].AssignLineNumber(i + 1);
        }

        _lines.AddRange(lines);
        Total = lines.Skip(1).Aggregate(lines[0].LineTotal, (sum, line) => sum + line.LineTotal);
    }

    public Guid Id { get; private set; }

    public Guid CustomerId { get; private set; }

    public OrderStatus Status { get; private set; }

    public CancellationReason? CancellationReason { get; private set; }

    public Money Total { get; private set; }

    public DateTimeOffset PlacedAt { get; private set; }

    /// <summary>The lines in the order the customer sent them (EF Core loads them in no particular order).</summary>
    public IReadOnlyList<OrderLine> Lines => [.. _lines.OrderBy(l => l.LineNumber)];

    /// <summary>A new order, <see cref="OrderStatus.Pending"/> until Catalog answers about its stock.</summary>
    public static Order Place(Guid id, Guid customerId, IReadOnlyList<OrderLine> lines, DateTimeOffset placedAt) =>
        new(id, customerId, lines ?? throw new ArgumentNullException(nameof(lines)), placedAt);

    /// <summary>Catalog reserved every line.</summary>
    public void ConfirmStockReserved()
    {
        EnsureStatus(OrderStatus.Pending, "confirmed");
        Status = OrderStatus.AwaitingPayment;
    }

    /// <summary>Catalog could not reserve every line and took nothing. The order is kept, so the customer can see what happened.</summary>
    public void RejectForLackOfStock()
    {
        EnsureStatus(OrderStatus.Pending, "rejected");
        Status = OrderStatus.Rejected;
    }

    /// <summary>Paying is only allowed while the order awaits payment. Checked before asking for the money.</summary>
    public void EnsureCanBePaid() => EnsureStatus(OrderStatus.AwaitingPayment, "paid");

    public void MarkPaid()
    {
        EnsureCanBePaid();
        Status = OrderStatus.Paid;
    }

    /// <summary>The payment was declined. The stock must come back: the application publishes <c>OrderCancelled</c>.</summary>
    public void DeclinePayment()
    {
        EnsureCanBePaid();
        Status = OrderStatus.Cancelled;
        CancellationReason = Domain.CancellationReason.PaymentDeclined;
    }

    /// <summary>The customer cancels. The stock must come back: the application publishes <c>OrderCancelled</c>.</summary>
    public void Cancel()
    {
        EnsureStatus(OrderStatus.AwaitingPayment, "cancelled");
        Status = OrderStatus.Cancelled;
        CancellationReason = Domain.CancellationReason.CustomerCancelled;
    }

    private void EnsureStatus(OrderStatus expected, string action)
    {
        if (Status != expected)
        {
            throw new BusinessRuleViolationException($"Order {Id} is {Status} and cannot be {action}.");
        }
    }
}
