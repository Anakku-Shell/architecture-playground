using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Shop.Micro.Messaging;

namespace Shop.Micro.Payments.Api.Data;

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

    /// <summary>Ordering's order, by id only: it lives in another service's database.</summary>
    public Guid OrderId { get; init; }

    public decimal Amount { get; init; }

    public PaymentStatus Status { get; init; }

    public DateTimeOffset ProcessedAt { get; init; }
}

/// <summary>The Payments service's DbContext, on its own database (<c>paymentsdb</c>), with the outbox and inbox. Guide: §8.2.</summary>
internal sealed class PaymentsDbContext(DbContextOptions<PaymentsDbContext> options) : DbContext(options)
{
    public DbSet<Payment> Payments => Set<Payment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Payment>(payment =>
        {
            payment.ToTable("payments");
            payment.HasKey(p => p.Id);
            payment.Property(p => p.Id).ValueGeneratedNever();
            // One payment per order, guaranteed by the database even if two deliveries race.
            payment.HasIndex(p => p.OrderId).IsUnique();
            payment.Property(p => p.Amount).HasPrecision(18, 2);
            payment.Property(p => p.Status).HasConversion<string>().HasMaxLength(30);
        });
        modelBuilder.AddOutboxAndInbox();
    }
}

/// <summary>Lets <c>dotnet ef</c> create this context without starting the service (it never connects).</summary>
internal sealed class PaymentsDbContextFactory : IDesignTimeDbContextFactory<PaymentsDbContext>
{
    public PaymentsDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<PaymentsDbContext>().UseNpgsql("Host=localhost;Database=paymentsdb").Options);
}

