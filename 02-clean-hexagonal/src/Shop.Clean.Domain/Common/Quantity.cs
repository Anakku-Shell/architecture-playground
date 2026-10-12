namespace Shop.Clean.Domain.Common;

/// <summary>How many units of one product an order line asks for: 1 to 1000.</summary>
public sealed record Quantity
{
    public const int Max = 1000;

    private Quantity(int value) => Value = value;

    public int Value { get; }

    public static Quantity Of(int value) =>
        value is < 1 or > Max
            ? throw new DomainValidationException($"Quantity must be between 1 and {Max}.")
            : new Quantity(value);

    /// <summary>
    /// Recreates a value read from storage without applying today's rules: data that was valid when it was
    /// written must still load after a rule changes. Only persistence adapters call it (an architecture test
    /// checks that). Guide: §5.2, "The database schema".
    /// </summary>
    public static Quantity Rehydrate(int value) => new(value);

    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
