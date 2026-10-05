using Shop.Modular.Ordering.Domain.Common;

namespace Shop.Modular.Ordering.Domain;

/// <summary>
/// One line of an order: part of the <see cref="Order"/> aggregate, never changed on its own. The product's
/// name and price are copied (a snapshot), so later catalog changes do not rewrite past orders. In 02 the
/// snapshot was taken from a <c>Product</c> object; here Ordering never sees one, only the plain values
/// Catalog publishes (<c>ProductSnapshot</c>).
/// </summary>
public sealed class OrderLine
{
    private OrderLine()
    {
        ProductName = null!;
        UnitPrice = null!;
        Quantity = null!;
        LineTotal = null!;
    }

    private OrderLine(Guid productId, ProductName productName, Money unitPrice, Quantity quantity)
    {
        ProductId = productId;
        ProductName = productName;
        UnitPrice = unitPrice;
        Quantity = quantity;
        LineTotal = unitPrice * quantity;
    }

    /// <summary>1, 2, 3… in the order the lines were placed. Assigned by <see cref="Order"/>.</summary>
    public int LineNumber { get; private set; }

    /// <summary>A reference to Catalog's product by id only: no object, and no foreign key across schemas.</summary>
    public Guid ProductId { get; private set; }

    public ProductName ProductName { get; private set; }

    public Money UnitPrice { get; private set; }

    public Quantity Quantity { get; private set; }

    public Money LineTotal { get; private set; }

    internal void AssignLineNumber(int lineNumber) => LineNumber = lineNumber;

    public static OrderLine Snapshot(Guid productId, ProductName productName, Money unitPrice, Quantity quantity)
    {
        ArgumentNullException.ThrowIfNull(productName);
        ArgumentNullException.ThrowIfNull(unitPrice);
        ArgumentNullException.ThrowIfNull(quantity);
        return new OrderLine(productId, productName, unitPrice, quantity);
    }
}
