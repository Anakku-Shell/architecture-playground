using Shop.Modular.BuildingBlocks;
using Shop.Modular.Ordering.Domain.Common;

namespace Shop.Modular.Ordering.Application.Common;

/// <summary>
/// Collects validation errors per input field, so one response lists every problem. The rules themselves
/// stay in the domain (<c>Money.Of</c>, <c>Quantity.Of</c>…): this class only remembers which input field each
/// value came from, something the domain neither knows nor should. Guide: §5.5 (version 02).
/// </summary>
public sealed class ValidationErrors
{
    private readonly Dictionary<string, List<string>> _errors = [];

    /// <summary>Runs <paramref name="create"/>; a <see cref="DomainValidationException"/> is recorded under <paramref name="field"/>.</summary>
    /// <returns>The created value, or <c>null</c> when it was invalid (only read it after <see cref="ThrowIfAny"/>).</returns>
    public T Capture<T>(string field, Func<T> create)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(create);
        try
        {
            return create();
        }
        catch (DomainValidationException ex)
        {
            Add(field, ex.Message);
            return null!;
        }
    }

    /// <summary>Runs a domain check that returns nothing; a <see cref="DomainValidationException"/> is recorded under <paramref name="field"/>.</summary>
    public void Check(string field, Action check)
    {
        ArgumentNullException.ThrowIfNull(check);
        try
        {
            check();
        }
        catch (DomainValidationException ex)
        {
            Add(field, ex.Message);
        }
    }

    public void Add(string field, string message)
    {
        if (!_errors.TryGetValue(field, out var messages))
        {
            _errors[field] = messages = [];
        }

        messages.Add(message);
    }

    public void ThrowIfAny()
    {
        if (_errors.Count > 0)
        {
            throw new ValidationException(_errors.ToDictionary(e => e.Key, e => e.Value.ToArray()));
        }
    }
}
