namespace Shop.Micro.Ordering.Application.Common;

// Failures the Ordering application reports. Each service owns its error types (services share no code
// but Contracts, Messaging and ServiceDefaults), and its HTTP adapter maps them to status codes.
// Guide: §8.6, "The error path".

/// <summary>The input breaks one or more validation rules. Becomes <c>400</c> with an <c>errors</c> member.</summary>
public sealed class ValidationException(IReadOnlyDictionary<string, string[]> errors)
    : Exception("One or more validation errors occurred.")
{
    /// <summary>Error messages per input field (camelCase, as in the JSON request).</summary>
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

/// <summary>The order does not exist. Becomes <c>404</c>.</summary>
public sealed class NotFoundException(string message) : Exception(message)
{
    public static NotFoundException Order(Guid id) => new($"Order {id} does not exist.");
}

/// <summary>
/// Someone else changed the order between our read and our save (optimistic concurrency). Over HTTP it
/// becomes <c>409</c>; in a message consumer the delivery fails and is retried against the new state.
/// </summary>
public sealed class ConcurrencyConflictException(string message, Exception? innerException = null) : Exception(message, innerException);

/// <summary>
/// The Catalog service did not answer in time, or failed. Becomes <c>503 Service Unavailable</c>: the request
/// was fine, another service was not, and the client may try again. Guide: §8.6, "Partial failure".
/// </summary>
public sealed class CatalogUnavailableException(string message, Exception innerException) : Exception(message, innerException);
