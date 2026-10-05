using Shop.Clean.Domain.Common;

namespace Shop.Clean.Domain.Ordering;

/// <summary>Where an order is in its lifecycle (Guide §3.11).</summary>
public enum OrderStatus
{
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
/// An order and its lines: an <b>aggregate root</b>. The lifecycle (Guide §3.11) is written here as
/// methods, and each method refuses a move the state machine does not allow. Nobody outside can set
/// <see cref="Status"/>: in 01 any code could. Guide: §5.2.
/// </summary>
public sealed class Order
{
    private readonly List<OrderLine> _lines = [];

    private Order()
    {
        Total = null!;
    }

    private Order(Guid id, Guid customerId, IReadOnlyList<OrderLine> lines, OrderStatus status, DateTimeOffset placedAt)
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
        Status = status;
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

    /// <summary>A new order whose stock is already reserved. Usually called by <see cref="OrderFulfillment.Place"/>.</summary>
    public static Order Place(Guid id, Guid customerId, IReadOnlyList<OrderLine> lines, DateTimeOffset placedAt) =>
        new(id, customerId, lines ?? throw new ArgumentNullException(nameof(lines)), OrderStatus.AwaitingPayment, placedAt);

    /// <summary>A new order that could not get its stock. It is kept, so the customer can see what happened.</summary>
    public static Order Reject(Guid id, Guid customerId, IReadOnlyList<OrderLine> lines, DateTimeOffset placedAt) =>
        new(id, customerId, lines ?? throw new ArgumentNullException(nameof(lines)), OrderStatus.Rejected, placedAt);

    public void MarkPaid()
    {
        EnsureAwaitingPayment("paid");
        Status = OrderStatus.Paid;
    }

    /// <summary>The payment was declined. The caller must give the stock back (see <see cref="OrderFulfillment"/>).</summary>
    public void DeclinePayment()
    {
        EnsureAwaitingPayment("paid");
        Status = OrderStatus.Cancelled;
        CancellationReason = Ordering.CancellationReason.PaymentDeclined;
    }

    /// <summary>The customer cancels. The caller must give the stock back (see <see cref="OrderFulfillment"/>).</summary>
    public void Cancel()
    {
        EnsureAwaitingPayment("cancelled");
        Status = OrderStatus.Cancelled;
        CancellationReason = Ordering.CancellationReason.CustomerCancelled;
    }

    /// <summary>Paying and cancelling are only allowed while the order awaits payment.</summary>
    public void EnsureAwaitingPayment(string action)
    {
        if (Status != OrderStatus.AwaitingPayment)
        {
            throw new BusinessRuleViolationException($"Order {Id} is {Status} and cannot be {action}.");
        }
    }
}
