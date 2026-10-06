using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Npgsql;
using Shop.Micro.Messaging;

namespace Shop.Micro.Catalog.Api.Data;

/// <summary>A product row. CRUD style: public setters, no behaviour; the rules are in <c>ProductRules</c>.</summary>
internal sealed class Product
{
    public Guid Id { get; set; }

    public required string Name { get; set; }

    public required string Sku { get; set; }

    public decimal Price { get; set; }

    public int Stock { get; set; }
}

/// <summary>
/// The Catalog service's DbContext, on its own database (<c>catalogdb</c>). Same products table as 04's
/// <c>catalog</c> schema, plus the outbox and inbox tables of the messaging building block: they must live
/// here, because the point is to write them in the same transaction as the stock. Guide: §8.2, "The databases".
/// </summary>
internal sealed class CatalogDbContext(DbContextOptions<CatalogDbContext> options) : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Product>(product =>
        {
            product.ToTable("products");
            product.HasKey(p => p.Id);
            product.Property(p => p.Id).ValueGeneratedNever();
            product.Property(p => p.Name).HasMaxLength(200);
            product.Property(p => p.Sku).HasMaxLength(50);
            product.HasIndex(p => p.Sku).IsUnique();
            product.Property(p => p.Price).HasPrecision(18, 2);
        });
        modelBuilder.AddOutboxAndInbox();
    }
}

/// <summary>Lets <c>dotnet ef</c> create this context without starting the service (it never connects).</summary>
internal sealed class CatalogDbContextFactory : IDesignTimeDbContextFactory<CatalogDbContext>
{
    public CatalogDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<CatalogDbContext>().UseNpgsql("Host=localhost;Database=catalogdb").Options);
}

internal static class DbUpdateExceptionExtensions
{
    /// <summary>The save broke a unique index (a duplicate SKU).</summary>
    public static bool IsUniqueViolation(this DbUpdateException exception) =>
        exception?.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}
