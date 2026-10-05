using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Shop.Slice.Api.Domain.Catalog;
using Shop.Slice.Api.Domain.Common;
using Shop.Slice.Api.Domain.Ordering;
using Shop.Slice.Api.Domain.Payments;

namespace Shop.Slice.Api.Infrastructure.Persistence;

// How the domain model is stored. Everything the database needs to know lives here, not in the domain:
// value converters (a Money is stored as a numeric column), the row-version column as a SHADOW property
// (EF Core knows it, the domain class does not), and order lines as owned rows of their order. The
// resulting tables are the same as in versions 01 and 02. Guide: §5.2 (introduced with version 02).

// Reading a column back uses Rehydrate, not Of: the row was valid when it was written, and re-running
// today's rules on it would make old data unreadable the day a rule tightens.
internal static class Converters
{
    public static readonly ValueConverter<Money, decimal> Money = new(m => m.Amount, v => Domain.Common.Money.Rehydrate(v));
    public static readonly ValueConverter<Sku, string> Sku = new(s => s.Value, v => Domain.Common.Sku.Rehydrate(v));
    public static readonly ValueConverter<ProductName, string> ProductName = new(n => n.Value, v => Domain.Common.ProductName.Rehydrate(v));
    public static readonly ValueConverter<Quantity, int> Quantity = new(q => q.Value, v => Domain.Common.Quantity.Rehydrate(v));

    /// <summary>
    /// PostgreSQL's <c>xmin</c> system column as an optimistic concurrency token, on a property the domain
    /// class does not have. Guide: §5.5, step 7.
    /// </summary>
    public static void HasRowVersion<T>(this EntityTypeBuilder<T> builder)
        where T : class => builder.Property<uint>("Version").IsRowVersion();
}

internal sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("products");
        builder.Property(p => p.Id).ValueGeneratedNever();
        builder.Property(p => p.Name).HasConversion(Converters.ProductName).HasMaxLength(Domain.Common.ProductName.MaxLength);
        builder.Property(p => p.Sku).HasConversion(Converters.Sku).HasMaxLength(Domain.Common.Sku.MaxLength);
        builder.HasIndex(p => p.Sku).IsUnique();
        builder.Property(p => p.Price).HasConversion(Converters.Money).HasPrecision(18, 2);
        builder.HasRowVersion();
    }
}

internal sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("orders");
        builder.Property(o => o.Id).ValueGeneratedNever();
        builder.Property(o => o.Status).HasConversion<string>().HasMaxLength(30);
        builder.Property(o => o.CancellationReason).HasConversion<string>().HasMaxLength(30);
        builder.Property(o => o.Total).HasConversion(Converters.Money).HasPrecision(18, 2);
        builder.HasRowVersion();

        // Lines are part of the Order aggregate: "owned" rows, loaded and saved with their order, never on
        // their own. EF Core fills the private _lines field; Id is a shadow key the domain does not need.
        builder.OwnsMany(o => o.Lines, line =>
        {
            line.ToTable("order_lines");
            line.WithOwner().HasForeignKey("OrderId");
            line.Property<Guid>("Id");
            line.HasKey("Id");
            // The table has no order of its own; Order.Lines sorts by this after loading.
            line.Property(l => l.LineNumber);
            line.Property(l => l.ProductName).HasConversion(Converters.ProductName).HasMaxLength(Domain.Common.ProductName.MaxLength);
            line.Property(l => l.UnitPrice).HasConversion(Converters.Money).HasPrecision(18, 2);
            line.Property(l => l.Quantity).HasConversion(Converters.Quantity);
            line.Property(l => l.LineTotal).HasConversion(Converters.Money).HasPrecision(18, 2);
        });
        builder.Navigation(o => o.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("payments");
        builder.Property(p => p.Id).ValueGeneratedNever();
        builder.HasIndex(p => p.OrderId).IsUnique();
        builder.Property(p => p.Amount).HasConversion(Converters.Money).HasPrecision(18, 2);
        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(30);
    }
}
