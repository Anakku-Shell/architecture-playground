namespace Shop.ContractTests;

/// <summary>
/// What a version provides to run the shared suite: an <see cref="HttpClient"/> pointing at its API,
/// and whether it finishes orders and payments inside the request or later.
/// Versions 01-04 implement it with WebApplicationFactory + Testcontainers;
/// version 05 with Aspire's testing host, pointing at the gateway.
/// </summary>
public interface IShopApi
{
    HttpClient CreateClient();

    /// <summary>
    /// <c>false</c> for 01-04: placing and paying an order finish inside the request (<c>201</c> / <c>200</c>).
    /// <c>true</c> for 05: they are accepted (<c>202</c>) and finish later through messaging.
    /// </summary>
    bool IsAsynchronous { get; }
}
