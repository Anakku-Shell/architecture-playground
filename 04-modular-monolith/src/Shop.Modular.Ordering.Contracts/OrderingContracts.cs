using Shop.Modular.BuildingBlocks;

namespace Shop.Modular.Ordering.Contracts;

// The public surface of the Ordering module: the facts it publishes. Guide: §7.3.

/// <summary>A product and how many units of it an order takes.</summary>
public sealed record OrderedItem(Guid ProductId, int Quantity);

/// <summary>A new order needs its stock. Catalog answers with <c>StockReserved</c> or <c>StockReservationFailed</c>.</summary>
public sealed record OrderPlaced(Guid OrderId, IReadOnlyList<OrderedItem> Items) : IIntegrationEvent;

/// <summary>An order that held stock was cancelled (by the customer or a declined payment): Catalog gives the units back.</summary>
public sealed record OrderCancelled(Guid OrderId, IReadOnlyList<OrderedItem> Items) : IIntegrationEvent;

/// <summary>An order must be charged. Payments answers with <c>PaymentSucceeded</c> or <c>PaymentDeclined</c>.</summary>
public sealed record PaymentRequested(Guid OrderId, decimal Amount) : IIntegrationEvent;
