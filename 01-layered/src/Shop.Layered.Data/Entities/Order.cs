namespace Shop.Layered.Data.Entities;

/// <summary>
/// An order as a table row with its lines. Anyone holding an <see cref="Order"/> can set
/// <see cref="Status"/> to anything: the lifecycle rules live in the Business services, not here
/// (an anemic model, Guide §3.7 and §4.2).
/// </summary>
public sealed class Order
{
    public Guid Id { get; set; }

    public Guid CustomerId { get; set; }

    public OrderStatus Status { get; set; }

    public CancellationReason? CancellationReason { get; set; }

    public decimal Total { get; set; }

    public DateTimeOffset PlacedAt { get; set; }

    public List<OrderLine> Lines { get; set; } = [];

    /// <summary>
    /// Mapped to PostgreSQL's <c>xmin</c> system column, which changes on every update of the row.
    /// EF Core adds it to the <c>WHERE</c> of each update, so two requests changing the same order
    /// at once cannot both win (optimistic concurrency, Guide §3.10).
    /// </summary>
    public uint Version { get; set; }
}
