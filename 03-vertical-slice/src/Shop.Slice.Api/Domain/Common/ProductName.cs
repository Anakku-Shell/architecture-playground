namespace Shop.Slice.Api.Domain.Common;

/// <summary>A product's display name: 1–200 characters, trimmed.</summary>
public sealed record ProductName
{
    public const int MaxLength = 200;

    private ProductName(string value) => Value = value;

    public string Value { get; }

    public static ProductName Of(string? value)
    {
        var trimmed = value?.Trim() ?? "";
        return trimmed.Length is < 1 or > MaxLength
            ? throw new DomainValidationException($"Name must have between 1 and {MaxLength} characters.")
            : new ProductName(trimmed);
    }

    /// <summary>
    /// Recreates a value read from storage without applying today's rules: data that was valid when it was
    /// written must still load after a rule changes. Only persistence adapters call it (an architecture test
    /// checks that). Guide: §5.2, "The database schema".
    /// </summary>
    public static ProductName Rehydrate(string value) => new(value);

    public override string ToString() => Value;
}
