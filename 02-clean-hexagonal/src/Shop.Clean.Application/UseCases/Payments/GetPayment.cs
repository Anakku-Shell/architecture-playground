using Shop.Clean.Application.Common;
using Shop.Clean.Application.Ports;
using Shop.Clean.Domain.Payments;

namespace Shop.Clean.Application.UseCases.Payments;

public sealed class GetPayment(IPaymentRepository payments)
{
    public async Task<Payment> ExecuteAsync(Guid? orderId, CancellationToken cancellationToken)
    {
        if (orderId is null || orderId == Guid.Empty)
        {
            var errors = new ValidationErrors();
            errors.Add("orderId", "The orderId query parameter is required.");
            errors.ThrowIfAny();
        }

        return await payments.GetByOrderAsync(orderId!.Value, cancellationToken)
            ?? throw new NotFoundException($"Order {orderId} has no payment.");
    }
}
