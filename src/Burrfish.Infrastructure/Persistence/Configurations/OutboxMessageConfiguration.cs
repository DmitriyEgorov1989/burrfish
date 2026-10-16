using Burrfish.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Burrfish.Infrastructure.Persistence.Configurations;

internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("outbox_messages", table =>
        {
            table.HasCheckConstraint("ck_outbox_attempt_count", "attempt_count >= 0");
            table.HasCheckConstraint(
                "ck_outbox_status",
                "status IN ('Pending', 'Processing', 'Sent', 'Failed')");
        });

        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).HasColumnName("id");
        builder.Property(entity => entity.DeduplicationKey).HasColumnName("deduplication_key").IsRequired();
        builder.Property(entity => entity.Type).HasColumnName("type").IsRequired();
        builder.Property(entity => entity.Payload).HasColumnName("payload").HasColumnType("jsonb").IsRequired();
        builder.Property(entity => entity.CreatedAt).HasColumnName("created_at");
        builder.Property(entity => entity.Status).HasColumnName("status").HasConversion<string>();
        builder.Property(entity => entity.AttemptCount).HasColumnName("attempt_count");
        builder.Property(entity => entity.NextAttemptAt).HasColumnName("next_attempt_at");
        builder.Property(entity => entity.LockedUntil).HasColumnName("locked_until");
        builder.Property(entity => entity.LastErrorCode).HasColumnName("last_error_code");
        builder.Property(entity => entity.LastErrorMessage).HasColumnName("last_error_message");
        builder.Property(entity => entity.Version).HasColumnName("xmin").IsRowVersion();

        builder.HasIndex(entity => entity.DeduplicationKey)
            .IsUnique()
            .HasDatabaseName("ux_outbox_deduplication_key");
        builder.HasIndex(entity => new { entity.Status, entity.NextAttemptAt })
            .HasDatabaseName("ix_outbox_pending");
    }
}
