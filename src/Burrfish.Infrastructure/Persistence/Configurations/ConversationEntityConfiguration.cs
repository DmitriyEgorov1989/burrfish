using Burrfish.Domain.Conversations;
using Burrfish.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Burrfish.Infrastructure.Persistence.Configurations;

internal sealed class ConversationEntityConfiguration : IEntityTypeConfiguration<ConversationEntity>
{
    public void Configure(EntityTypeBuilder<ConversationEntity> builder)
    {
        builder.ToTable("conversations", table => table.HasCheckConstraint(
            "ck_conversations_pending_need",
            "(state = 'AwaitingHandoffConfirmation' AND pending_need IS NOT NULL AND btrim(pending_need) <> '') " +
            "OR (state <> 'AwaitingHandoffConfirmation' AND pending_need IS NULL)"));

        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).HasColumnName("id");
        builder.Property(entity => entity.ExternalConversationId)
            .HasColumnName("external_conversation_id")
            .IsRequired();
        builder.Property(entity => entity.SenderId).HasColumnName("sender_id").IsRequired();
        builder.Property(entity => entity.Username).HasColumnName("username");
        builder.Property(entity => entity.CreatedAt).HasColumnName("created_at");
        builder.Property(entity => entity.Version).HasColumnName("xmin").IsRowVersion();

        builder.HasIndex(entity => entity.ExternalConversationId)
            .IsUnique()
            .HasDatabaseName("ux_conversations_external_id");

        builder.OwnsOne(entity => entity.Model, owned =>
        {
            owned.WithOwner().HasForeignKey("Id");
            owned.Property<Guid>("Id").HasColumnName("id");
            owned.HasKey("Id");
            owned.Property(model => model.State)
                .HasColumnName("state")
                .HasConversion<string>()
                .IsRequired();
            owned.Property(model => model.PendingNeed).HasColumnName("pending_need");
            owned.Ignore(model => model.CanReply);
            owned.Property<uint>("Version").HasColumnName("xmin").IsRowVersion();
        });

        builder.Navigation(entity => entity.Model).IsRequired();
    }
}
