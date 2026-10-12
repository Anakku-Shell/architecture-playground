using Shop.Clean.Application.Ports;

namespace Shop.Clean.Application.Common;

/// <summary>
/// Runs a use case's load-decide-save steps again when the save loses an optimistic concurrency check.
/// Version 01 let the database decide stock atomically with a conditional <c>UPDATE</c>. Here the rule
/// lives in <c>Product.Reserve</c>, in memory, so the database can only detect afterwards that the row
/// changed (its <c>xmin</c>): the losing request reloads fresh data and decides again. Guide: §5.5, step 7.
/// </summary>
public static class ConcurrencyRetry
{
    /// <summary>
    /// A request loses only when another request committed a change to the same row between its read and
    /// its save. With N requests writing one product, a request can lose at most N - 1 times, and far fewer
    /// when stock runs out (a Rejected order writes no product row). 15 covers the contract's ten racing
    /// customers with headroom; after that the request fails with <c>409</c> instead of retrying forever.
    /// </summary>
    public const int MaxAttempts = 15;

    public static async Task<T> ExecuteAsync<T>(IUnitOfWork unitOfWork, Func<Task<T>> attempt)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(attempt);
        for (var attemptNumber = 1; ; attemptNumber++)
        {
            try
            {
                return await attempt();
            }
            catch (ConcurrencyConflictException) when (attemptNumber < MaxAttempts)
            {
                unitOfWork.DiscardChanges();
            }
            catch (ConcurrencyConflictException)
            {
                throw new ConflictException($"The data changed {MaxAttempts} times while this request ran. Try again.");
            }
        }
    }
}
