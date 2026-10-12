using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Shop.Modular.BuildingBlocks.Infrastructure.Persistence;
using Shop.Modular.Ordering.Domain;
using Shop.Modular.Ordering.Domain.Common;

namespace Shop.Modular.Ordering.Infrastructure.Persistence;

/// <summary>
/// The Ordering module's DbContext, mapped to its own schema (<c>ordering</c>). The mapping is version 02's
/// (value converters, owned lines) minus the <c>xmin</c> row version: this version locks the order row
/// instead (see <see cref="OrderRepository.GetForUpdateAsync"/>). Guide: §7.2, "The database schemas".
/// </summary>
internal sealed class OrderingDbContext(DbContextOptions<OrderingDbContext> options) : DbContext(options)
{
    public const string Schema = "ordering";

    public DbSet<Order> Orders => Set<Order>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.Entity<Order>(order =>
        {
            order.ToTable("orders");
            order.Property(o => o.Id).ValueGeneratedNever();
            order.Property(o => o.Status).HasConversion<string>().HasMaxLength(30);
            order.Property(o => o.CancellationReason).HasConversion<string>().HasMaxLength(30);
            order.Property(o => o.Total).HasConversion(Converters.Money).HasPrecision(18, 2);

            // Lines are part of the Order aggregate: owned rows, loaded and saved with their order. ProductId
            // points at Catalog's product by value only: a foreign key across module schemas would tie the
            // two modules' tables together, and moving Catalog to its own database (05) would break it.
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
    }

    // Reading a column back uses Rehydrate, not Of: the row was valid when it was written (Guide §5.2).
    private static class Converters
    {
        public static readonly ValueConverter<Money, decimal> Money = new(m => m.Amount, v => Domain.Common.Money.Rehydrate(v));
        public static readonly ValueConverter<ProductName, string> ProductName = new(n => n.Value, v => Domain.Common.ProductName.Rehydrate(v));
        public static readonly ValueConverter<Quantity, int> Quantity = new(q => q.Value, v => Domain.Common.Quantity.Rehydrate(v));
    }
}

/// <summary>Lets <c>dotnet ef</c> create this context without starting the Host.</summary>
internal sealed class OrderingDbContextFactory : IDesignTimeDbContextFactory<OrderingDbContext>
{
    public OrderingDbContext CreateDbContext(string[] args) =>
        new(DatabaseServiceCollectionExtensions.DesignTimeOptions<OrderingDbContext>(OrderingDbContext.Schema));
}
