using Shop.Slice.Api.Domain.Catalog;
using Shop.Slice.Api.Domain.Common;

namespace Shop.Slice.Api.Domain.Ordering;

/// <summary>
/// One line of an order: part of the <see cref="Order"/> aggregate, never changed on its own. The product's
/// name and price are copied (a snapshot), so later catalog changes do not rewrite past orders.
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

    public Guid ProductId { get; private set; }

    public ProductName ProductName { get; private set; }

    public Money UnitPrice { get; private set; }

    public Quantity Quantity { get; private set; }

    public Money LineTotal { get; private set; }

    internal void AssignLineNumber(int lineNumber) => LineNumber = lineNumber;

    public static OrderLine Snapshot(Product product, Quantity quantity)
    {
        ArgumentNullException.ThrowIfNull(product);
        ArgumentNullException.ThrowIfNull(quantity);
        return new OrderLine(product.Id, product.Name, product.Price, quantity);
    }
}
