using Burrfish.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Burrfish.Infrastructure.Persistence.Configurations;

internal sealed class LeadEntityConfiguration : IEntityTypeConfiguration<LeadEntity>
{
    public void Configure(EntityTypeBuilder<LeadEntity> builder)
    {
        builder.ToTable("leads", table => table.HasCheckConstraint(
            "ck_lead_notification_status",
            "notification_status IN ('Pending', 'Sent', 'Failed')"));

        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).HasColumnName("id");
        builder.Property(entity => entity.ConversationId).HasColumnName("conversation_id");
        builder.Property(entity => entity.HandoffKey).HasColumnName("handoff_key");
        builder.Property(entity => entity.SenderId).HasColumnName("sender_id").IsRequired();
        builder.Property(entity => entity.Username).HasColumnName("username");
        builder.Property(entity => entity.Need).HasColumnName("need").IsRequired();
        builder.Property(entity => entity.CreatedAt).HasColumnName("created_at");
        builder.Property(entity => entity.NotificationStatus)
            .HasColumnName("notification_status")
            .HasConversion<string>();
        builder.Property(entity => entity.Version).HasColumnName("xmin").IsRowVersion();

        builder.HasIndex(entity => entity.HandoffKey)
            .IsUnique()
            .HasDatabaseName("ux_leads_handoff_key");

        builder.HasOne(entity => entity.Conversation)
            .WithMany()
            .HasForeignKey(entity => entity.ConversationId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
