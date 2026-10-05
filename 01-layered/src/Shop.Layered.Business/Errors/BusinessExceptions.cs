namespace Shop.Layered.Business.Errors;

// The Business layer reports failures with exceptions; the Api layer turns each kind into an HTTP
// status (ErrorHandling/BusinessExceptionHandler). The Business layer itself knows nothing about HTTP.
// Guide: §4.5, "The error path".

/// <summary>The input breaks a validation rule. Becomes <c>400</c> with an <c>errors</c> member.</summary>
public sealed class ValidationException : Exception
{
    public ValidationException(IReadOnlyDictionary<string, string[]> errors)
        : base("One or more validation errors occurred.") => Errors = errors;

    public ValidationException(string field, string error)
        : this(new Dictionary<string, string[]> { [field] = [error] })
    {
    }

    /// <summary>Error messages per input field (camelCase, as in the JSON request).</summary>
    public IReadOnlyDictionary<string, string[]> Errors { get; }
}

/// <summary>The requested product, order or payment does not exist. Becomes <c>404</c>.</summary>
public sealed class NotFoundException(string message) : Exception(message);

/// <summary>
/// The request is well formed but a business rule forbids it (duplicate SKU, stock below zero, paying a
/// cancelled order…). Becomes <c>409</c>.
/// </summary>
public sealed class BusinessRuleException(string message) : Exception(message);
