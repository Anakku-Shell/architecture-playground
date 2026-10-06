namespace Shop.Micro.Ordering.Domain.Common;

// The domain reports broken rules with its own exceptions. It knows nothing about HTTP: the service's HTTP adapter
// decides that a validation error is a 400 and a rule violation a 409. Guide: §8.6, "The error path".

/// <summary>Base class of every exception thrown by the domain model.</summary>
public abstract class DomainException(string message) : Exception(message);

/// <summary>A value that can never be valid, whatever the state: a price with three decimals, an empty SKU.</summary>
public sealed class DomainValidationException(string message) : DomainException(message);

/// <summary>A valid request that the current state forbids: paying a cancelled order, stock below zero.</summary>
public sealed class BusinessRuleViolationException(string message) : DomainException(message);
