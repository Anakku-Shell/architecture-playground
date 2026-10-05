using Microsoft.EntityFrameworkCore;
using Shop.Layered.Data.Entities;

namespace Shop.Layered.Data;

/// <summary>
/// The single EF Core context of the layered version: one database (<c>shop_layered</c>), all tables.
/// The Business layer uses it directly, with no repository in between: that is the layered style
/// of this version. Guide: §4.2.
/// </summary>
public sealed class ShopDbContext(DbContextOptions<ShopDbContext> options) : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();

    public DbSet<Order> Orders => Set<Order>();

    public DbSet<Payment> Payments => Set<Payment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Product>(product =>
        {
            product.ToTable("products");
            product.Property(p => p.Name).HasMaxLength(200);
            product.Property(p => p.Sku).HasMaxLength(50);
            // The database is the last line of defence for "SKUs are unique": two requests that both
            // pass the service's check at the same moment still cannot insert the same SKU.
            product.HasIndex(p => p.Sku).IsUnique();
            product.Property(p => p.Price).HasPrecision(18, 2);
        });

        modelBuilder.Entity<Order>(order =>
        {
            order.ToTable("orders");
            order.Property(o => o.Status).HasConversion<string>().HasMaxLength(30);
            order.Property(o => o.CancellationReason).HasConversion<string>().HasMaxLength(30);
            order.Property(o => o.Total).HasPrecision(18, 2);
            order.Property(o => o.Version).IsRowVersion();
            order.HasMany(o => o.Lines).WithOne().HasForeignKey(l => l.OrderId);
        });

        modelBuilder.Entity<OrderLine>(line =>
        {
            line.ToTable("order_lines");
            line.Property(l => l.ProductName).HasMaxLength(200);
            line.Property(l => l.UnitPrice).HasPrecision(18, 2);
            line.Property(l => l.LineTotal).HasPrecision(18, 2);
        });

        modelBuilder.Entity<Payment>(payment =>
        {
            payment.ToTable("payments");
            payment.HasIndex(p => p.OrderId).IsUnique();
            payment.Property(p => p.Amount).HasPrecision(18, 2);
            payment.Property(p => p.Status).HasConversion<string>().HasMaxLength(30);
        });
    }
}
