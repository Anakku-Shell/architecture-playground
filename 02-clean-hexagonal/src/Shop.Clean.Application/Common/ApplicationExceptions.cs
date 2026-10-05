namespace Shop.Clean.Application.Common;

// Failures the use cases report, besides the domain's own exceptions. Like the domain, they know nothing
// about HTTP: the Api adapter maps each one to a status code. Guide: §5.5, "The error path".

/// <summary>The input breaks one or more validation rules. Becomes <c>400</c> with an <c>errors</c> member.</summary>
public sealed class ValidationException(IReadOnlyDictionary<string, string[]> errors)
    : Exception("One or more validation errors occurred.")
{
    /// <summary>Error messages per input field (camelCase, as in the JSON request).</summary>
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

/// <summary>The requested product, order or payment does not exist. Becomes <c>404</c>.</summary>
public sealed class NotFoundException(string message) : Exception(message);

/// <summary>The request conflicts with data that exists or with a concurrent request (duplicate SKU). Becomes <c>409</c>.</summary>
public sealed class ConflictException(string message) : Exception(message);
