namespace Shop.Micro.Ordering.Domain.Common;

/// <summary>
/// A positive amount of the shop's single currency, with at most two decimals. A <b>value object</b>
/// (Guide §3.7): defined only by its value, immutable, and valid by construction. Every price, line
/// total, order total and payment amount in the domain is a <see cref="Money"/>, so "at most two
/// decimals" is checked in exactly one place. Guide: §5.2.
/// </summary>
public sealed record Money
{
    private Money(decimal amount) => Amount = amount;

    public decimal Amount { get; }

    public static Money Of(decimal amount)
    {
        if (amount <= 0)
        {
            throw new DomainValidationException("An amount must be greater than zero.");
        }

        if (decimal.Round(amount, 2) != amount)
        {
            throw new DomainValidationException("An amount can have at most two decimals.");
        }

        return new Money(amount);
    }

    public static Money operator +(Money left, Money right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        return new Money(left.Amount + right.Amount);
    }

    public static Money operator *(Money price, Quantity quantity)
    {
        ArgumentNullException.ThrowIfNull(price);
        ArgumentNullException.ThrowIfNull(quantity);
        return new Money(price.Amount * quantity.Value);
    }

    public static Money Add(Money left, Money right) => left + right;

    public static Money Multiply(Money price, Quantity quantity) => price * quantity;

    /// <summary>
    /// Recreates a value read from storage without applying today's rules: data that was valid when it was
    /// written must still load after a rule changes. Only persistence adapters call it (an architecture test
    /// checks that). Guide: §5.2, "The database schema".
    /// </summary>
    public static Money Rehydrate(decimal amount) => new(amount);

    public override string ToString() => Amount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
}
