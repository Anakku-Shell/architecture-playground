using System.Net;
using System.Text.Json;
using Aspire.Hosting.Testing;
using Npgsql;
using RabbitMQ.Client;
using Shop.ContractTests;
using Shop.Micro.Contracts.Payments;
using Xunit;

namespace Shop.Micro.ContractTests;

/// <summary>
/// Not part of the shared contract: properties only this version has. They reach past the gateway, sending
/// messages straight to the broker and reading the services' own tables, to pin the two defences against
/// duplicates (Review Focus): the inbox for the SAME message delivered twice, and business checks for a
/// second message with the same meaning. Guide: §8.4, "The idempotent inbox".
/// </summary>
public sealed class DuplicateMessageTests(MicroShopApi api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task RedeliveredReserveStock_ReservesOnce()
    {
        var shop = new ShopClient(api.CreateClient(), isAsynchronous: true);
        var product = await shop.CreateProduct(stock: 5);
        var placed = await shop.PlaceOrder((product.Id, 2));
        await shop.WaitForSettled(placed.Id);
        Assert.Equal(3, (await shop.GetProduct(product.Id)).Stock);

        // The exact message Ordering sent (same id, same body), delivered again, as after a lost acknowledgement.
        var (messageId, payload) = await ReadOutboxRowAsync("orderingdb", "ReserveStock", placed.Id);
        await PublishAsync("ReserveStock", messageId, payload);

        // Catalog handles its queue in order, so once a later order is settled the duplicate has been seen.
        var marker = await shop.CreateProduct(stock: 1);
        await shop.WaitForSettled((await shop.PlaceOrder((marker.Id, 1))).Id);

        Assert.Equal(3, (await shop.GetProduct(product.Id)).Stock);
    }

    [Fact]
    public async Task SecondProcessPaymentForAnOrder_ChargesOnce_AndAnswersWithTheSamePayment()
    {
        var shop = new ShopClient(api.CreateClient(), isAsynchronous: true);
        var product = await shop.CreateProduct(price: 10m);
        var placed = await shop.PlaceOrder((product.Id, 1));
        await shop.WaitForSettled(placed.Id);
        Assert.Equal(OrderStatus.Paid, (await shop.Pay(placed.Id)).Status);
        var payment = await shop.GetPayment(placed.Id);

        // A NEW message (new id) asking to charge the same order: the inbox cannot recognise it.
        var messageId = Guid.NewGuid();
        await PublishAsync(nameof(ProcessPayment), messageId, JsonSerializer.Serialize(new ProcessPayment(placed.Id, 10m), JsonSerializerOptions.Web));
        await WaitUntilAsync(async () => await CountAsync("paymentsdb", """SELECT count(*) FROM inbox_messages WHERE "MessageId" = @p""", messageId) == 1);

        Assert.Equal(1, await CountAsync("paymentsdb", """SELECT count(*) FROM payments WHERE "OrderId" = @p""", placed.Id));
        Assert.Equal(2, await CountAsync("paymentsdb", $"""SELECT count(*) FROM outbox_messages WHERE "Type" = 'PaymentSucceeded' AND "Payload"->>'paymentId' = '{payment.Id}'""", placed.Id));
        Assert.Equal(OrderStatus.Paid, (await shop.GetOrder(placed.Id)).Status);
        Assert.Equal(payment.Id, (await shop.GetPayment(placed.Id)).Id);
    }

    /// <summary>The price lookup is for Ordering only: the gateway routes the public API, nothing else.</summary>
    [Fact]
    public async Task InternalEndpoints_AreNotReachableThroughTheGateway()
    {
        using var client = api.CreateClient();

        var response = await client.GetAsync(new Uri($"/internal/product-snapshots?ids={Guid.NewGuid()}", UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private async Task<(Guid MessageId, string Payload)> ReadOutboxRowAsync(string database, string type, Guid orderId)
    {
        await using var connection = await OpenAsync(database);
        await using var command = new NpgsqlCommand("""SELECT "Id", "Payload"::text FROM outbox_messages WHERE "Type" = @type AND "Payload"->>'orderId' = @order""", connection);
        command.Parameters.AddWithValue("type", type);
        command.Parameters.AddWithValue("order", orderId.ToString());
        await using var reader = await command.ExecuteReaderAsync(Ct);
        Assert.True(await reader.ReadAsync(Ct), $"No {type} row for order {orderId}.");
        return (reader.GetGuid(0), reader.GetString(1));
    }

    private async Task<long> CountAsync(string database, string sql, Guid parameter)
    {
        await using var connection = await OpenAsync(database);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("p", parameter);
        return (long)(await command.ExecuteScalarAsync(Ct))!;
    }

    private async Task<NpgsqlConnection> OpenAsync(string database)
    {
        var connection = new NpgsqlConnection(await api.App.GetConnectionStringAsync(database, Ct));
        await connection.OpenAsync(Ct);
        return connection;
    }

    /// <summary>Publishes like the outbox dispatcher does: exchange <c>shop</c>, routing key and type = message name.</summary>
    private async Task PublishAsync(string type, Guid messageId, string payload)
    {
        var factory = new ConnectionFactory { Uri = new Uri((await api.App.GetConnectionStringAsync("messaging", Ct))!) };
        await using var connection = await factory.CreateConnectionAsync(Ct);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: Ct);
        var properties = new BasicProperties { MessageId = messageId.ToString(), Type = type, ContentType = "application/json", Persistent = true };
        await channel.BasicPublishAsync("shop", type, mandatory: true, properties, System.Text.Encoding.UTF8.GetBytes(payload), Ct);
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
        while (!await condition())
        {
            Assert.True(DateTimeOffset.UtcNow < deadline, "Timed out waiting for the message to be handled.");
            await Task.Delay(100, Ct);
        }
    }
}
