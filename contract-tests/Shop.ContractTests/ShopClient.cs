using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace Shop.ContractTests;

/// <summary>
/// A thin typed client over the public API. "Raw" methods return the HTTP response so tests can
/// assert status codes; the others assert success and return the parsed body.
/// </summary>
public sealed class ShopClient(HttpClient http, bool isAsynchronous)
{
    /// <summary>See <see cref="IShopApi.IsAsynchronous"/>.</summary>
    public bool IsAsynchronous { get; } = isAsynchronous;

    /// <summary>
    /// Placing and paying an order answer <paramref name="synchronousCode"/> (201 / 200) in 01-04 and
    /// <c>202 Accepted</c> in 05, where the work finishes later. The only intended difference in the contract.
    /// </summary>
    public async Task AssertCompletedOrAccepted(HttpResponseMessage response, HttpStatusCode synchronousCode)
    {
        ArgumentNullException.ThrowIfNull(response);
        var expected = IsAsynchronous ? HttpStatusCode.Accepted : synchronousCode;
        if (response.StatusCode != expected)
        {
            var body = await response.Content.ReadAsStringAsync(Ct);
            Assert.Fail($"Expected {(int)expected} but got {(int)response.StatusCode}: {body}");
        }
    }

    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>A SKU no other test uses, so tests never collide on the unique-SKU rule.</summary>
    public static string UniqueSku() => $"T-{Guid.NewGuid():N}";

    // ---- Products ----

    public Task<HttpResponseMessage> CreateProductRaw(object body) =>
        http.PostAsJsonAsync("/api/products", body, Ct);

    public async Task<ProductResponse> CreateProduct(string? name = null, decimal price = 10.00m, int stock = 10)
    {
        var response = await CreateProductRaw(new { name = name ?? "Test product", sku = UniqueSku(), price, initialStock = stock });
        return await ReadSuccess<ProductResponse>(response);
    }

    public Task<HttpResponseMessage> GetProductRaw(Guid id) =>
        http.GetAsync(new Uri($"/api/products/{id}", UriKind.Relative), Ct);

    public async Task<ProductResponse> GetProduct(Guid id) =>
        await ReadSuccess<ProductResponse>(await GetProductRaw(id));

    public async Task<IReadOnlyList<ProductResponse>> ListProducts() =>
        await ReadSuccess<List<ProductResponse>>(await http.GetAsync(new Uri("/api/products", UriKind.Relative), Ct));

    public Task<HttpResponseMessage> ChangePriceRaw(Guid id, decimal price) =>
        http.PutAsJsonAsync($"/api/products/{id}/price", new { price }, Ct);

    public Task<HttpResponseMessage> AdjustStockRaw(Guid id, int quantity) =>
        http.PostAsJsonAsync($"/api/products/{id}/stock-adjustments", new { quantity }, Ct);

    /// <summary>
    /// Polls a product until <paramref name="until"/> holds. Stock released by a cancellation
    /// changes asynchronously in version 05; in 01-04 the first read already matches.
    /// </summary>
    public Task<ProductResponse> WaitForProduct(Guid id, Func<ProductResponse, bool> until, TimeSpan? timeout = null) =>
        Poll(() => GetProduct(id), until, timeout, p => $"stock {p.Stock}");

    // ---- Orders ----

    public Task<HttpResponseMessage> PlaceOrderRaw(object body) =>
        http.PostAsJsonAsync("/api/orders", body, Ct);

    public async Task<OrderResponse> PlaceOrder(params (Guid ProductId, int Quantity)[] lines)
    {
        var body = new
        {
            customerId = Guid.NewGuid(),
            lines = lines.Select(l => new { productId = l.ProductId, quantity = l.Quantity }).ToArray(),
        };
        var response = await PlaceOrderRaw(body);
        await AssertCompletedOrAccepted(response, HttpStatusCode.Created);
        return await ReadSuccess<OrderResponse>(response);
    }

    /// <summary>Pays an order and waits for the outcome: <c>Paid</c>, or <c>Cancelled</c> if declined.</summary>
    public async Task<OrderResponse> Pay(Guid orderId)
    {
        await AssertCompletedOrAccepted(await PayRaw(orderId), HttpStatusCode.OK);
        return await WaitForPaymentOutcome(orderId);
    }

    public Task<HttpResponseMessage> GetOrderRaw(Guid id) =>
        http.GetAsync(new Uri($"/api/orders/{id}", UriKind.Relative), Ct);

    public async Task<OrderResponse> GetOrder(Guid id) =>
        await ReadSuccess<OrderResponse>(await GetOrderRaw(id));

    public Task<HttpResponseMessage> PayRaw(Guid orderId) =>
        http.PostAsync(new Uri($"/api/orders/{orderId}/pay", UriKind.Relative), content: null, Ct);

    public Task<HttpResponseMessage> CancelRaw(Guid orderId) =>
        http.PostAsync(new Uri($"/api/orders/{orderId}/cancel", UriKind.Relative), content: null, Ct);

    /// <summary>Polls an order until <paramref name="until"/> holds (default timeout 15 s).</summary>
    public Task<OrderResponse> WaitForOrder(Guid id, Func<OrderResponse, bool> until, TimeSpan? timeout = null) =>
        Poll(() => GetOrder(id), until, timeout, o => $"status {o.Status}");

    /// <summary>
    /// Waits until the order is no longer in a transient state (<c>Pending</c>, <c>PaymentPending</c>).
    /// Those states only exist in version 05, so in 01-04 this returns on the first read.
    /// </summary>
    public Task<OrderResponse> WaitForSettled(Guid id) =>
        WaitForOrder(id, o => o.Status is not (OrderStatus.Pending or OrderStatus.PaymentPending));

    /// <summary>
    /// Waits for the result of a payment: <c>Paid</c> or <c>Cancelled</c>. Waiting for "not transient" is not
    /// enough here: in 05 the order may still read <c>AwaitingPayment</c> right after the 202.
    /// </summary>
    public Task<OrderResponse> WaitForPaymentOutcome(Guid id) =>
        WaitForOrder(id, o => o.Status is OrderStatus.Paid or OrderStatus.Cancelled);

    // ---- Payments ----

    public Task<HttpResponseMessage> GetPaymentRaw(Guid? orderId) =>
        http.GetAsync(new Uri(orderId is null ? "/api/payments" : $"/api/payments?orderId={orderId}", UriKind.Relative), Ct);

    public async Task<PaymentResponse> GetPayment(Guid orderId) =>
        await ReadSuccess<PaymentResponse>(await GetPaymentRaw(orderId));

    // ---- Helpers ----

    public static async Task<T> ReadSuccess<T>(HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(Ct);
            Assert.Fail($"Expected success but got {(int)response.StatusCode} {response.StatusCode}: {body}");
        }

        var value = await response.Content.ReadFromJsonAsync<T>(Ct);
        Assert.NotNull(value);
        return value;
    }

    private static async Task<T> Poll<T>(Func<Task<T>> read, Func<T, bool> until, TimeSpan? timeout, Func<T, string> describe)
    {
        var deadline = DateTimeOffset.UtcNow + (timeout ?? DefaultTimeout);
        while (true)
        {
            var value = await read();
            if (until(value))
            {
                return value;
            }

            if (DateTimeOffset.UtcNow >= deadline)
            {
                Assert.Fail($"Condition not met before the timeout; last seen: {describe(value)}.");
            }

            await Task.Delay(PollInterval, Ct);
        }
    }
}
