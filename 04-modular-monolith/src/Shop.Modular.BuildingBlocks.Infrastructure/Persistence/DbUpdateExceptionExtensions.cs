using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Shop.Modular.BuildingBlocks.Infrastructure.Persistence;

public static class DbUpdateExceptionExtensions
{
    /// <summary>The save broke a unique index (a duplicate SKU, a second payment for one order).</summary>
    public static bool IsUniqueViolation(this DbUpdateException exception) =>
        exception?.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}
