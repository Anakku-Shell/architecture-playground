using System.Data.Common;
using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Shop.ContractTests;
using Shop.Modular.BuildingBlocks;
using Shop.Modular.BuildingBlocks.Infrastructure.Persistence;
using Shop.Modular.Ordering.Contracts;
using Xunit;

namespace Shop.Modular.ContractTests;

/// <summary>
/// Not part of the shared contract: a property only this version has. Two modules write in one request, and
/// a failure in the second undoes the first, because both use the request's one transaction. Guide: §7.3.
/// </summary>
public sealed class CrossModuleTransactionTests(ModularShopApi api)
{
    [Fact]
    public async Task FailureInALaterModule_RollsBackEarlierModules()
    {
        var ct = TestContext.Current.CancellationToken;
        var shop = new ShopClient(api.CreateClient(), isAsynchronous: false);
        var product = await shop.CreateProduct(stock: 5);
        int? stockSeenInsideTheTransaction = null;

        await using (var scope = api.Services.CreateAsyncScope())
        {
            var transaction = scope.ServiceProvider.GetRequiredService<SharedTransaction>();
            var bus = scope.ServiceProvider.GetRequiredService<IEventBus>();
            var connection = scope.ServiceProvider.GetRequiredService<DbConnection>();

            // OrderPlaced for an order that does not exist: Catalog reserves 2 units, answers StockReserved,
            // and Ordering's consumer fails because it cannot find the order.
            await Assert.ThrowsAsync<NotFoundException>(() => transaction.ExecuteAsync(async token =>
            {
                try
                {
                    await bus.PublishAsync(new OrderPlaced(Guid.NewGuid(), [new OrderedItem(product.Id, 2)]), token);
                }
                finally
                {
                    stockSeenInsideTheTransaction = await ReadStockAsync(connection, product.Id, token);
                }

                return 0;
            }, ct));
        }

        Assert.Equal(3, stockSeenInsideTheTransaction);
        Assert.Equal(5, (await shop.GetProduct(product.Id)).Stock);
    }

    /// <summary>
    /// Catalog does not trust another module to have merged repeated products: two lines of 2 units of a
    /// product with 3 in stock must fail, not leave the stock at -1.
    /// </summary>
    [Fact]
    public async Task RepeatedProductInOneEvent_IsReservedAsItsTotal()
    {
        var ct = TestContext.Current.CancellationToken;
        var shop = new ShopClient(api.CreateClient(), isAsynchronous: false);
        var product = await shop.CreateProduct(stock: 3);
        int? stockSeenInsideTheTransaction = null;

        await using (var scope = api.Services.CreateAsyncScope())
        {
            var transaction = scope.ServiceProvider.GetRequiredService<SharedTransaction>();
            var bus = scope.ServiceProvider.GetRequiredService<IEventBus>();
            var connection = scope.ServiceProvider.GetRequiredService<DbConnection>();

            // The order does not exist, so Ordering's consumer of Catalog's answer fails either way; what
            // matters is what Catalog did before answering.
            await Assert.ThrowsAsync<NotFoundException>(() => transaction.ExecuteAsync(async token =>
            {
                try
                {
                    await bus.PublishAsync(new OrderPlaced(Guid.NewGuid(), [new OrderedItem(product.Id, 2), new OrderedItem(product.Id, 2)]), token);
                }
                finally
                {
                    stockSeenInsideTheTransaction = await ReadStockAsync(connection, product.Id, token);
                }

                return 0;
            }, ct));
        }

        Assert.Equal(3, stockSeenInsideTheTransaction);
    }

    private static async Task<int> ReadStockAsync(DbConnection connection, Guid productId, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """SELECT "Stock" FROM catalog.products WHERE "Id" = @id""";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "id";
        parameter.Value = productId;
        command.Parameters.Add(parameter);
        return Convert.ToInt32(await command.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture);
    }
}
