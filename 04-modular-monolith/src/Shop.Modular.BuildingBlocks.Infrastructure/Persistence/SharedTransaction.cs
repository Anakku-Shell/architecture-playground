using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;

namespace Shop.Modular.BuildingBlocks.Infrastructure.Persistence;

/// <summary>
/// One database transaction for every module that takes part in a request. Each module has its own
/// DbContext and schema, but all of them use the same connection of the request (see
/// <see cref="DatabaseServiceCollectionExtensions.AddSharedDatabase"/>), so one transaction on that
/// connection covers them all. This is what one database buys a modular monolith: an order and the stock
/// it reserves are committed together, or not at all. Version 05 has three databases and loses it.
/// Guide: §7.3.
/// </summary>
public sealed class SharedTransaction(DbConnection connection, IEnumerable<ModuleDbContext> modules)
{
    private DbTransaction? _current;

    /// <summary>
    /// Runs <paramref name="work"/> inside one transaction and commits it; any exception rolls everything back.
    /// Re-entrant: called while a transaction is already open, it joins it.
    /// </summary>
    public async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(work);
        if (_current is not null)
        {
            return await work(cancellationToken);
        }

        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        // READ COMMITTED, PostgreSQL's default. The rows that must not change under us are locked explicitly
        // (SELECT … FOR UPDATE), so a stricter level is not needed. Guide: §7.5.
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        _current = transaction;
        var contexts = modules.Select(m => m.Context).ToList();
        try
        {
            // Every module's DbContext uses this transaction instead of starting its own on SaveChanges.
            foreach (var context in contexts)
            {
                await context.Database.UseTransactionAsync(transaction, cancellationToken);
            }

            var result = await work(cancellationToken);
            // Not the request's token: once the work is done, a client hanging up must not turn it into a rollback.
            await transaction.CommitAsync(CancellationToken.None);
            return result;
        }
        finally
        {
            // Without a commit, disposing the transaction rolls it back.
            foreach (var context in contexts)
            {
                await context.Database.UseTransactionAsync(null, CancellationToken.None);
            }

            _current = null;
        }
    }
}

/// <summary>A module's DbContext, registered so <see cref="SharedTransaction"/> can enlist it.</summary>
public sealed record ModuleDbContext(DbContext Context);
