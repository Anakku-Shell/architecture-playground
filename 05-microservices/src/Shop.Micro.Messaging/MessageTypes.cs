using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Shop.Micro.Contracts;

namespace Shop.Micro.Messaging;

/// <summary>
/// The message types one service consumes, by name: the name arrives with the message (AMQP <c>type</c>
/// property) and picks both the class to deserialize into and the consumer to call.
/// </summary>
internal sealed class MessageTypeRegistry
{
    private readonly Dictionary<string, Registration> _byName = new(StringComparer.Ordinal);

    public IEnumerable<string> Names => _byName.Keys;

    /// <summary>The wire name of a message type, also its routing key.</summary>
    public static string NameOf(Type type) => type.Name;

    public void Add<TMessage>()
        where TMessage : class, IIntegrationMessage =>
        _byName[NameOf(typeof(TMessage))] = new Registration(
            typeof(TMessage),
            (services, message, ct) => services.GetRequiredService<IMessageConsumer<TMessage>>().ConsumeAsync((TMessage)message, ct));

    public bool TryGet(string? name, out Registration registration) =>
        _byName.TryGetValue(name ?? "", out registration!);

    internal sealed record Registration(Type Type, Func<IServiceProvider, object, CancellationToken, Task> Dispatch);
}

/// <summary>The wire format: UTF-8 JSON with the web defaults (camelCase), like the HTTP API.</summary>
internal static class MessageSerializer
{
    public static string Serialize(IIntegrationMessage message) =>
        JsonSerializer.Serialize(message, message.GetType(), JsonSerializerOptions.Web);

    public static object Deserialize(ReadOnlySpan<byte> body, Type type) =>
        JsonSerializer.Deserialize(body, type, JsonSerializerOptions.Web)
        ?? throw new JsonException($"The body of a {type.Name} message is empty.");

    public static byte[] ToBytes(string payload) => Encoding.UTF8.GetBytes(payload);
}

/// <summary>
/// Spans for the two halves of a message hop: "publish" in the dispatcher and "process" in the consumer.
/// The trace id travels in the outbox row and then in a <c>traceparent</c> header (the W3C Trace Context
/// format HTTP uses), so the Aspire dashboard shows one order as one trace across all services. Guide: §8.3.
/// </summary>
internal static class MessagingTracing
{
    public const string TraceParentHeader = "traceparent";

    public static readonly ActivitySource Source = new("Shop.Micro.Messaging");

    public static Activity? StartPublish(OutboxMessage message, string exchange) =>
        Start($"{message.Type} publish", ActivityKind.Producer, message.TraceParent, message.Type, message.Id.ToString(), exchange);

    public static Activity? StartProcess(string type, string? messageId, string? traceParent, string queue) =>
        Start($"{type} process", ActivityKind.Consumer, traceParent, type, messageId, queue);

    public static string? ReadTraceParent(IDictionary<string, object?>? headers) =>
        headers?.TryGetValue(TraceParentHeader, out var value) == true && value is byte[] bytes ? Encoding.UTF8.GetString(bytes) : null;

    private static Activity? Start(string name, ActivityKind kind, string? parent, string type, string? messageId, string destination) =>
        Source.StartActivity(
            name,
            kind,
            parent,
            [
                new("messaging.system", "rabbitmq"),
                new("messaging.destination.name", destination),
                new("messaging.rabbitmq.destination.routing_key", type),
                new("messaging.message.id", messageId),
            ]);
}
