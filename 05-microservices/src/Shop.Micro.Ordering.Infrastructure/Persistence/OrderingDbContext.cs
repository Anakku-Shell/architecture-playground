using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Shop.Micro.Messaging;
using Shop.Micro.Ordering.Domain;
using Shop.Micro.Ordering.Domain.Common;

namespace Shop.Micro.Ordering.Infrastructure.Persistence;

/// <summary>
/// The Ordering service's DbContext, on its own database (<c>orderingdb</c>). The mapping is version 02's
/// (value converters, owned lines, the <c>xmin</c> row version), plus the outbox and inbox tables. Version 04
/// locked the order row for the whole request; a lock cannot span services and messages, so 05 goes back to
/// optimistic concurrency: a save fails if the row changed since it was read. Guide: §8.2, "The databases".
/// </summary>
internal sealed class OrderingDbContext(DbContextOptions<OrderingDbContext> options) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Order>(order =>
        {
            order.ToTable("orders");
            order.Property(o => o.Id).ValueGeneratedNever();
            order.Property(o => o.Status).HasConversion<string>().HasMaxLength(30);
            order.Property(o => o.CancellationReason).HasConversion<string>().HasMaxLength(30);
            order.Property(o => o.Total).HasConversion(Converters.Money).HasPrecision(18, 2);

            // PostgreSQL's xmin system column as the concurrency token, on a property the domain never sees.
            order.Property<uint>("Version").IsRowVersion();

            // Lines are part of the Order aggregate: owned rows, loaded and saved with their order. ProductId
            // points at Catalog's product by value only: Catalog's rows live in another service's database.
            order.OwnsMany(o => o.Lines, line =>
            {
                line.ToTable("order_lines");
                line.WithOwner().HasForeignKey("OrderId");
                line.Property<Guid>("Id");
                line.HasKey("Id");
                line.Property(l => l.LineNumber);
                line.Property(l => l.ProductName).HasConversion(Converters.ProductName).HasMaxLength(ProductName.MaxLength);
                line.Property(l => l.UnitPrice).HasConversion(Converters.Money).HasPrecision(18, 2);
                line.Property(l => l.Quantity).HasConversion(Converters.Quantity);
                line.Property(l => l.LineTotal).HasConversion(Converters.Money).HasPrecision(18, 2);
            });
            order.Navigation(o => o.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
        });
        modelBuilder.AddOutboxAndInbox();
    }

    // Reading a column back uses Rehydrate, not Of: the row was valid when it was written (Guide §5.2).
    private static class Converters
    {
        public static readonly ValueConverter<Money, decimal> Money = new(m => m.Amount, v => Domain.Common.Money.Rehydrate(v));
        public static readonly ValueConverter<ProductName, string> ProductName = new(n => n.Value, v => Domain.Common.ProductName.Rehydrate(v));
        public static readonly ValueConverter<Quantity, int> Quantity = new(q => q.Value, v => Domain.Common.Quantity.Rehydrate(v));
    }
}

/// <summary>Lets <c>dotnet ef</c> create this context without starting the service (it never connects).</summary>
internal sealed class OrderingDbContextFactory : IDesignTimeDbContextFactory<OrderingDbContext>
{
    public OrderingDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<OrderingDbContext>().UseNpgsql("Host=localhost;Database=orderingdb").Options);
}
