using Shop.Micro.Ordering.Domain.Common;

namespace Shop.Micro.Ordering.Domain;

/// <summary>Where an order is in its lifecycle (Guide §3.11).</summary>
public enum OrderStatus
{
    /// <summary>Placed, waiting for Catalog to answer about its stock.</summary>
    Pending,
    AwaitingPayment,
    Rejected,

    /// <summary>The payment was asked for, waiting for Payments to answer. Version 05 only.</summary>
    PaymentPending,
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
/// An order and its lines: the <b>aggregate root</b> of the Ordering service, as in 04 with one state more.
/// Answers now arrive later, through messages, so the waiting is part of the model: <see cref="OrderStatus.Pending"/>
/// until Catalog answers, <see cref="OrderStatus.PaymentPending"/> until Payments does. The status is also the
/// state of the saga that drives the order (Guide §8.5).
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
    public void ConfirmStockReserved() => MoveFrom(OrderStatus.Pending, OrderStatus.AwaitingPayment, "confirmed");

    /// <summary>Catalog could not reserve every line and took nothing.</summary>
    public void RejectForLackOfStock() => MoveFrom(OrderStatus.Pending, OrderStatus.Rejected, "rejected");

    /// <summary>
    /// The customer asked to pay; Payments is asked to charge. From now on the order can be neither paid
    /// again nor cancelled until Payments answers.
    /// </summary>
    public void RequestPayment() => MoveFrom(OrderStatus.AwaitingPayment, OrderStatus.PaymentPending, "paid");

    public void MarkPaid() => MoveFrom(OrderStatus.PaymentPending, OrderStatus.Paid, "marked paid");

    /// <summary>The payment was declined. The stock must come back: the saga sends <c>ReleaseStock</c>.</summary>
    public void DeclinePayment()
    {
        MoveFrom(OrderStatus.PaymentPending, OrderStatus.Cancelled, "declined");
        CancellationReason = Domain.CancellationReason.PaymentDeclined;
    }

    /// <summary>The customer cancels. The stock must come back: the use case sends <c>ReleaseStock</c>.</summary>
    public void Cancel()
    {
        MoveFrom(OrderStatus.AwaitingPayment, OrderStatus.Cancelled, "cancelled");
        CancellationReason = Domain.CancellationReason.CustomerCancelled;
    }

    private void MoveFrom(OrderStatus expected, OrderStatus next, string action)
    {
        if (Status != expected)
        {
            throw new BusinessRuleViolationException($"Order {Id} is {Status} and cannot be {action}.");
        }

        Status = next;
    }
}
