using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Shop.Modular.BuildingBlocks.Infrastructure.Persistence;

namespace Shop.Modular.Payments.Data;

/// <summary>What the payment gateway answered.</summary>
internal enum PaymentStatus
{
    Approved,
    Declined,
}

/// <summary>The record of one charge attempt for an order. Created once, never changed.</summary>
internal sealed class Payment
{
    public Guid Id { get; init; }

    /// <summary>Ordering's order, by id only: no foreign key across schemas.</summary>
    public Guid OrderId { get; init; }

    public decimal Amount { get; init; }

    public PaymentStatus Status { get; init; }

    public DateTimeOffset ProcessedAt { get; init; }
}

/// <summary>The Payments module's own DbContext, mapped to its own schema (<c>payments</c>). Guide: §7.2.</summary>
internal sealed class PaymentsDbContext(DbContextOptions<PaymentsDbContext> options) : DbContext(options)
{
    public const string Schema = "payments";

    public DbSet<Payment> Payments => Set<Payment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.Entity<Payment>(payment =>
        {
            payment.ToTable("payments");
            payment.HasKey(p => p.Id);
            payment.Property(p => p.Id).ValueGeneratedNever();
            // One payment per order, guaranteed by the database even if two requests race.
            payment.HasIndex(p => p.OrderId).IsUnique();
            payment.Property(p => p.Amount).HasPrecision(18, 2);
            payment.Property(p => p.Status).HasConversion<string>().HasMaxLength(30);
        });
    }
}

/// <summary>Lets <c>dotnet ef</c> create this context without starting the Host.</summary>
internal sealed class PaymentsDbContextFactory : IDesignTimeDbContextFactory<PaymentsDbContext>
{
    public PaymentsDbContext CreateDbContext(string[] args) =>
        new(DatabaseServiceCollectionExtensions.DesignTimeOptions<PaymentsDbContext>(PaymentsDbContext.Schema));
}
