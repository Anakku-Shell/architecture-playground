namespace Shop.Modular.BuildingBlocks;

// Failures any module can report. They know nothing about HTTP: the Host's error handler maps each one
// to a status code, the same way for every module. Guide: §7.5, "The error path".

/// <summary>The input breaks one or more validation rules. Becomes <c>400</c> with an <c>errors</c> member.</summary>
public sealed class ValidationException(IReadOnlyDictionary<string, string[]> errors)
    : Exception("One or more validation errors occurred.")
{
    public ValidationException(string field, string message)
        : this(new Dictionary<string, string[]> { [field] = [message] })
    {
    }

    /// <summary>Error messages per input field (camelCase, as in the JSON request).</summary>
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

/// <summary>The requested product, order or payment does not exist. Becomes <c>404</c>.</summary>
public sealed class NotFoundException(string message) : Exception(message);

/// <summary>The request conflicts with existing data or a business rule (duplicate SKU, stock out of range). Becomes <c>409</c>.</summary>
public sealed class ConflictException(string message) : Exception(message);
