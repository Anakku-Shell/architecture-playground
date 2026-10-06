using Microsoft.EntityFrameworkCore;

namespace Shop.Micro.Messaging;

/// <summary>A message waiting to be published (<see cref="SentAt"/> null) or already published. Guide: §8.4.</summary>
public sealed class OutboxMessage
{
    /// <summary>Also the message id the consumer's inbox remembers, so a re-send is recognised as a duplicate.</summary>
    public Guid Id { get; init; }

    /// <summary>The message type name, which is also its routing key.</summary>
    public required string Type { get; init; }

    /// <summary>The message as JSON.</summary>
    public required string Payload { get; init; }

    public DateTimeOffset OccurredAt { get; init; }

    /// <summary>The trace the message was sent from (W3C <c>traceparent</c>), so the hop shows up in that trace.</summary>
    public string? TraceParent { get; init; }

    public DateTimeOffset? SentAt { get; set; }

    /// <summary>Failed publish attempts. After <see cref="MessagingOptions.MaxAttempts"/> the row is left for a person.</summary>
    public int Attempts { get; set; }

    /// <summary>Why the last publish failed (for the person who looks at a stuck row).</summary>
    public string? LastError { get; set; }
}

/// <summary>A message this service has already handled. Its id is the primary key: the same id cannot be handled twice.</summary>
public sealed class InboxMessage
{
    public Guid MessageId { get; init; }

    public required string Type { get; init; }

    public DateTimeOffset ProcessedAt { get; init; }
}

public static class MessagingModelBuilderExtensions
{
    /// <summary>
    /// Adds the <c>outbox_messages</c> and <c>inbox_messages</c> tables to a service's model. They live in the
    /// service's own database because the whole point is to share its transactions.
    /// </summary>
    public static ModelBuilder AddOutboxAndInbox(this ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.Entity<OutboxMessage>(outbox =>
        {
            outbox.ToTable("outbox_messages");
            outbox.HasKey(m => m.Id);
            outbox.Property(m => m.Id).ValueGeneratedNever();
            outbox.Property(m => m.Type).HasMaxLength(200);
            outbox.Property(m => m.Payload).HasColumnType("jsonb");
            outbox.Property(m => m.TraceParent).HasMaxLength(100);
            outbox.Property(m => m.LastError).HasMaxLength(500);

            // The dispatcher only ever looks for unsent rows, oldest first; a partial index keeps that cheap
            // however many sent rows pile up.
            outbox.HasIndex(m => m.OccurredAt).HasFilter("\"SentAt\" IS NULL");
        });
        modelBuilder.Entity<InboxMessage>(inbox =>
        {
            inbox.ToTable("inbox_messages");
            inbox.HasKey(m => m.MessageId);
            inbox.Property(m => m.MessageId).ValueGeneratedNever();
            inbox.Property(m => m.Type).HasMaxLength(200);
        });
        return modelBuilder;
    }
}
