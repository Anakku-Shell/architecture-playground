using Microsoft.EntityFrameworkCore;
using Shop.Slice.Api.Domain.Catalog;
using Shop.Slice.Api.Domain.Ordering;
using Shop.Slice.Api.Domain.Payments;

namespace Shop.Slice.Api.Infrastructure.Persistence;

/// <summary>
/// The EF Core context, used by the slices directly: no repository, no port, no unit-of-work interface.
/// A <c>DbContext</c> already is a unit of work with a repository per <c>DbSet</c>; in this style another
/// layer of abstraction over it would add files without adding a decision. Guide: §6.2.
/// </summary>
internal sealed class ShopDbContext(DbContextOptions<ShopDbContext> options) : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();

    public DbSet<Order> Orders => Set<Order>();

    public DbSet<Payment> Payments => Set<Payment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ShopDbContext).Assembly);
}
