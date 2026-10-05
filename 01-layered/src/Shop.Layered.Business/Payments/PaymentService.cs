using Microsoft.EntityFrameworkCore;
using Shop.Layered.Business.Errors;
using Shop.Layered.Data;
using Shop.Layered.Data.Entities;

namespace Shop.Layered.Business.Payments;

/// <summary>Charges orders and reads payments. Works on the EF entities and the DbContext directly.</summary>
public sealed class PaymentService(ShopDbContext db, FakePaymentGateway gateway, TimeProvider time)
{
    /// <summary>
    /// Charges the order's total and adds the <see cref="Payment"/> row to the context, without saving:
    /// <see cref="Ordering.OrderService.PayAsync"/> saves it together with the order's new status, so both
    /// are stored in one transaction or not at all. Services sharing one scoped DbContext, and agreeing on
    /// who calls <c>SaveChanges</c>, is how layered code composes work. Guide: §4.5.
    /// </summary>
    public Payment Charge(Order order)
    {
        ArgumentNullException.ThrowIfNull(order);

        var payment = new Payment
        {
            Id = Guid.CreateVersion7(),
            OrderId = order.Id,
            Amount = order.Total,
            Status = gateway.Charge(order.Total),
            ProcessedAt = time.GetUtcNow(),
        };
        db.Payments.Add(payment);
        return payment;
    }

    public async Task<Payment> GetByOrderAsync(Guid? orderId, CancellationToken cancellationToken)
    {
        if (orderId is null || orderId == Guid.Empty)
        {
            throw new ValidationException("orderId", "The orderId query parameter is required.");
        }

        return await db.Payments.AsNoTracking().FirstOrDefaultAsync(p => p.OrderId == orderId, cancellationToken)
            ?? throw new NotFoundException($"Order {orderId} has no payment.");
    }
}
