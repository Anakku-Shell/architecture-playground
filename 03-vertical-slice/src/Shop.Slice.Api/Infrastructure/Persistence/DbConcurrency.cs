using Microsoft.EntityFrameworkCore;
using Npgsql;
using Shop.Slice.Api.Common;

namespace Shop.Slice.Api.Infrastructure.Persistence;

/// <summary>
/// Helpers the command slices share around saving. Same strategy as version 02 (optimistic <c>xmin</c>
/// check + bounded retry), but written against EF Core directly: there is no unit-of-work port to hide it
/// behind. Guide: §6.5.
/// </summary>
internal static class DbConcurrency
{
    /// <summary>See version 02's <c>ConcurrencyRetry</c> for why 15 is enough for the contract's races.</summary>
    public const int MaxAttempts = 15;

    /// <summary>Runs load-decide-save again, from fresh data, when the save loses an optimistic concurrency check.</summary>
    public static async Task<T> RetryOnConflictAsync<T>(this ShopDbContext db, Func<Task<T>> attempt)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(attempt);
        for (var attemptNumber = 1; ; attemptNumber++)
        {
            try
            {
                return await attempt();
            }
            catch (DbUpdateConcurrencyException) when (attemptNumber < MaxAttempts)
            {
                db.ChangeTracker.Clear();
            }
            catch (DbUpdateConcurrencyException)
            {
                throw new ConflictException($"The data changed {MaxAttempts} times while this request ran. Try again.");
            }
        }
    }

    public static bool IsUniqueViolation(this DbUpdateException exception) =>
        exception?.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}
