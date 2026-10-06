namespace Shop.Micro.Contracts.Payments;

// What the Payments service accepts and answers. Guide: §8.4.

/// <summary>Command: charge an order. Answered with <see cref="PaymentSucceeded"/> or <see cref="PaymentDeclined"/>.</summary>
public sealed record ProcessPayment(Guid OrderId, decimal Amount) : IIntegrationMessage;

/// <summary>Event: the order was charged.</summary>
public sealed record PaymentSucceeded(Guid OrderId, Guid PaymentId) : IIntegrationMessage;

/// <summary>Event: the payment provider refused the charge.</summary>
public sealed record PaymentDeclined(Guid OrderId, Guid PaymentId) : IIntegrationMessage;
