using Microsoft.EntityFrameworkCore;
using Shop.Clean.Domain.Catalog;
using Shop.Clean.Domain.Ordering;
using Shop.Clean.Domain.Payments;

namespace Shop.Clean.Infrastructure.Persistence;

/// <summary>
/// The EF Core context, now an implementation detail of Infrastructure (it is <c>internal</c>): nothing
/// outside this project can see it. It maps the domain aggregates directly, with all the mapping in
/// <c>Configurations/</c>, so the domain classes carry no attributes. Guide: §5.2.
/// </summary>
internal sealed class ShopDbContext(DbContextOptions<ShopDbContext> options) : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();

    public DbSet<Order> Orders => Set<Order>();

    public DbSet<Payment> Payments => Set<Payment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ShopDbContext).Assembly);
}
