namespace Shop.Micro.Contracts.Catalog;

// What the Catalog service accepts and answers. In an orchestrated saga the receiver owns the message
// types: Catalog publishes the "API" of its stock, and Ordering (the orchestrator) uses it. Compare with
// 04, where Ordering owned OrderPlaced and Catalog reacted to it. Guide: §8.4.

/// <summary>One product and how many units of it an order takes or gives back.</summary>
public sealed record OrderedItem(Guid ProductId, int Quantity);

/// <summary>Command: take these units for an order, every line or none. Answered with <see cref="StockReserved"/> or <see cref="StockReservationFailed"/>.</summary>
public sealed record ReserveStock(Guid OrderId, IReadOnlyList<OrderedItem> Items) : IIntegrationMessage;

/// <summary>Command: give back the units of an order that will not be paid. The saga's <b>compensation</b>; never refused.</summary>
public sealed record ReleaseStock(Guid OrderId, IReadOnlyList<OrderedItem> Items) : IIntegrationMessage;

/// <summary>Event: every line of the order was reserved.</summary>
public sealed record StockReserved(Guid OrderId) : IIntegrationMessage;

/// <summary>Event: at least one line could not be reserved, so nothing was taken.</summary>
public sealed record StockReservationFailed(Guid OrderId) : IIntegrationMessage;

/// <summary>
/// What Ordering reads from Catalog over HTTP when an order is placed (<c>GET /internal/product-snapshots</c>).
/// A synchronous question with a timeout, because the customer is waiting for the answer (ADR 0006).
/// </summary>
public sealed record ProductSnapshot(Guid Id, string Name, decimal Price);
