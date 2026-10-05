using Shop.Layered.Api.Models;
using Shop.Layered.Business.Payments;

namespace Shop.Layered.Api.Endpoints;

public static class PaymentEndpoints
{
    public static IEndpointRouteBuilder MapPaymentEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/payments", async (Guid? orderId, PaymentService service, CancellationToken ct) =>
            TypedResults.Ok(PaymentResponse.From(await service.GetByOrderAsync(orderId, ct))));

        return app;
    }
}
