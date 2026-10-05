using Shop.Modular.BuildingBlocks;

namespace Shop.Modular.Payments.Contracts;

// The public surface of the Payments module: the facts it publishes. Guide: §7.3.

/// <summary>The order was charged.</summary>
public sealed record PaymentSucceeded(Guid OrderId, Guid PaymentId) : IIntegrationEvent;

/// <summary>The provider refused the charge.</summary>
public sealed record PaymentDeclined(Guid OrderId, Guid PaymentId) : IIntegrationEvent;
