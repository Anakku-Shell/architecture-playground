namespace Shop.Clean.Domain.Common;

/// <summary>
/// Stock Keeping Unit, the shop's own product code: 1–50 characters, case-insensitive. It is stored
/// upper-case, so <c>"abc-1"</c> and <c>"ABC-1"</c> are the same <see cref="Sku"/> (records compare by value).
/// </summary>
public sealed record Sku
{
    public const int MaxLength = 50;

    private Sku(string value) => Value = value;

    public string Value { get; }

    public static Sku Of(string? value)
    {
        var normalized = value?.Trim().ToUpperInvariant() ?? "";
        return normalized.Length is < 1 or > MaxLength
            ? throw new DomainValidationException($"SKU must have between 1 and {MaxLength} characters.")
            : new Sku(normalized);
    }

    /// <summary>
    /// Recreates a value read from storage without applying today's rules: data that was valid when it was
    /// written must still load after a rule changes. Only persistence adapters call it (an architecture test
    /// checks that). Guide: §5.2, "The database schema".
    /// </summary>
    public static Sku Rehydrate(string value) => new(value);

    public override string ToString() => Value;
}
