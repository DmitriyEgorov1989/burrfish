using Burrfish.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Burrfish.Infrastructure.Persistence.Configurations;

internal sealed class InboxEventConfiguration : IEntityTypeConfiguration<InboxEvent>
{
    public void Configure(EntityTypeBuilder<InboxEvent> builder)
    {
        builder.ToTable("inbox_events", table =>
        {
            table.HasCheckConstraint("ck_inbox_attempt_count", "attempt_count >= 0");
            table.HasCheckConstraint(
                "ck_inbox_status",
                "status IN ('Pending', 'Processing', 'Processed', 'Failed')");
        });

        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).HasColumnName("id");
        builder.Property(entity => entity.ExternalEventId).HasColumnName("external_event_id").IsRequired();
        builder.Property(entity => entity.Payload).HasColumnName("payload").IsRequired();
        builder.Property(entity => entity.ReceivedAt).HasColumnName("received_at");
        builder.Property(entity => entity.Status).HasColumnName("status").HasConversion<string>();
        builder.Property(entity => entity.AttemptCount).HasColumnName("attempt_count");
        builder.Property(entity => entity.NextAttemptAt).HasColumnName("next_attempt_at");
        builder.Property(entity => entity.LockedUntil).HasColumnName("locked_until");
        builder.Property(entity => entity.LastErrorCode).HasColumnName("last_error_code");
        builder.Property(entity => entity.Version).HasColumnName("xmin").IsRowVersion();

        builder.HasIndex(entity => entity.ExternalEventId)
            .IsUnique()
            .HasDatabaseName("ux_inbox_external_event_id");
        builder.HasIndex(entity => new { entity.Status, entity.NextAttemptAt })
            .HasDatabaseName("ix_inbox_pending");
    }
}
