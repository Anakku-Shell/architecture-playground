namespace Shop.Layered.Data.Entities;

/// <summary>
/// One line of an order. <see cref="ProductName"/> and <see cref="UnitPrice"/> are a snapshot taken
/// when the order is placed, so later catalog changes do not rewrite past orders.
/// </summary>
public sealed class OrderLine
{
    public Guid Id { get; set; }

    public Guid OrderId { get; set; }

    /// <summary>
    /// 1, 2, 3… in the order the customer sent the lines. A table has no order of its own and the ids are
    /// not sequential within a millisecond, so reading lines back in request order needs this column.
    /// </summary>
    public int LineNumber { get; set; }

    public Guid ProductId { get; set; }

    public string ProductName { get; set; } = "";

    public decimal UnitPrice { get; set; }

    public int Quantity { get; set; }

    public decimal LineTotal { get; set; }
}
