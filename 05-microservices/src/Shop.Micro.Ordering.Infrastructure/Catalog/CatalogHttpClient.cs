using System.Globalization;
using System.Net.Http.Json;
using Shop.Micro.Contracts.Catalog;
using Shop.Micro.Ordering.Application.Common;
using Shop.Micro.Ordering.Application.Ports;

namespace Shop.Micro.Ordering.Infrastructure.Catalog;

/// <summary>
/// The driven adapter of <see cref="ICatalogClient"/>: a synchronous HTTP call to the Catalog service. Its
/// base address is <c>http://catalog</c>, a name that service discovery turns into a real address, and the
/// service defaults wrap it in retries, timeouts and a circuit breaker. Whatever goes wrong (no answer in
/// time, an error, the breaker open) becomes <see cref="CatalogUnavailableException"/>: the order is not
/// placed and the client gets <c>503</c>. Guide: §8.6, "Partial failure", and ADR 0006.
/// </summary>
internal sealed class CatalogHttpClient(HttpClient http) : ICatalogClient
{
    /// <summary>The longest a customer waits for Catalog, retries included.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    public async Task<IReadOnlyList<ProductSnapshot>> GetProductsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ids);
        var query = string.Join('&', ids.Select(id => string.Create(CultureInfo.InvariantCulture, $"ids={id}")));
        try
        {
            return await http.GetFromJsonAsync<List<ProductSnapshot>>(new Uri($"/internal/product-snapshots?{query}", UriKind.Relative), cancellationToken)
                ?? [];
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            throw new CatalogUnavailableException("The Catalog service did not answer; the order was not placed. Try again later.", ex);
        }
    }
}
