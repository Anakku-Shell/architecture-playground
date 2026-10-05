using Shop.Clean.Api.Models;
using Shop.Clean.Application.UseCases.Catalog;
using Shop.Clean.Application.UseCases.Ordering;
using Shop.Clean.Application.UseCases.Payments;

namespace Shop.Clean.Api.Endpoints;

// The DRIVING ADAPTER for HTTP (Guide §3.4): it turns a request into a call on a use case and the result
// into a response. One endpoint, one use case; no decision is taken here. Guide: §5.2.

public static class ProductEndpoints
{
    public static IEndpointRouteBuilder MapProductEndpoints(this IEndpointRouteBuilder app)
    {
        var products = app.MapGroup("/api/products");

        products.MapPost("/", async (CreateProductRequest request, CreateProduct useCase, CancellationToken ct) =>
        {
            var product = await useCase.ExecuteAsync(new CreateProductCommand(request.Name, request.Sku, request.Price, request.InitialStock), ct);
            return TypedResults.Created($"/api/products/{product.Id}", ProductResponse.From(product));
        });

        products.MapGet("/", async (ListProducts useCase, CancellationToken ct) =>
            TypedResults.Ok((await useCase.ExecuteAsync(ct)).Select(ProductResponse.From)));

        products.MapGet("/{id:guid}", async (Guid id, GetProduct useCase, CancellationToken ct) =>
            TypedResults.Ok(ProductResponse.From(await useCase.ExecuteAsync(id, ct))));

        products.MapPut("/{id:guid}/price", async (Guid id, ChangePriceRequest request, ChangeProductPrice useCase, CancellationToken ct) =>
            TypedResults.Ok(ProductResponse.From(await useCase.ExecuteAsync(new ChangeProductPriceCommand(id, request.Price), ct))));

        products.MapPost("/{id:guid}/stock-adjustments", async (Guid id, AdjustStockRequest request, AdjustStock useCase, CancellationToken ct) =>
            TypedResults.Ok(ProductResponse.From(await useCase.ExecuteAsync(new AdjustStockCommand(id, request.Quantity), ct))));

        return app;
    }
}

public static class OrderEndpoints
{
    public static IEndpointRouteBuilder MapOrderEndpoints(this IEndpointRouteBuilder app)
    {
        var orders = app.MapGroup("/api/orders");

        orders.MapPost("/", async (PlaceOrderRequest request, PlaceOrder useCase, CancellationToken ct) =>
        {
            // A null element ("lines": [null]) becomes an empty line, which the use case rejects with 400.
            var lines = request.Lines?.Select(l => new PlaceOrderLine(l?.ProductId ?? Guid.Empty, l?.Quantity ?? 0)).ToList();
            var order = await useCase.ExecuteAsync(new PlaceOrderCommand(request.CustomerId, lines), ct);
            return TypedResults.Created($"/api/orders/{order.Id}", OrderResponse.From(order));
        });

        orders.MapGet("/{id:guid}", async (Guid id, GetOrder useCase, CancellationToken ct) =>
            TypedResults.Ok(OrderResponse.From(await useCase.ExecuteAsync(id, ct))));

        orders.MapPost("/{id:guid}/pay", async (Guid id, PayOrder useCase, CancellationToken ct) =>
            TypedResults.Ok(OrderResponse.From(await useCase.ExecuteAsync(id, ct))));

        orders.MapPost("/{id:guid}/cancel", async (Guid id, CancelOrder useCase, CancellationToken ct) =>
            TypedResults.Ok(OrderResponse.From(await useCase.ExecuteAsync(id, ct))));

        return app;
    }
}

public static class PaymentEndpoints
{
    public static IEndpointRouteBuilder MapPaymentEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/payments", async (Guid? orderId, GetPayment useCase, CancellationToken ct) =>
            TypedResults.Ok(PaymentResponse.From(await useCase.ExecuteAsync(orderId, ct))));

        return app;
    }
}
