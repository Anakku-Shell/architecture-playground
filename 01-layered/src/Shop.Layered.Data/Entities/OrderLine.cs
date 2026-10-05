namespace Shop.Layered.Data.Entities;

/// <summary>
/// One line of an order. <see cref="ProductName"/> and <see cref="UnitPrice"/> are a snapshot taken
/// when the order is placed, so later catalog changes do not rewrite past orders.
/// </summary>
public sealed class OrderLine
{
    public Guid Id { get; set; }

    public Guid OrderId { get; set; }

    public Guid ProductId { get; set; }

    public string ProductName { get; set; } = "";

    public decimal UnitPrice { get; set; }

    public int Quantity { get; set; }

    public decimal LineTotal { get; set; }
}
