using Burrfish.Application.Persistence;
using Burrfish.Domain.Conversations;
using Burrfish.Infrastructure.Persistence.Entities;

namespace Burrfish.Infrastructure.Persistence;

/// <summary>Добавляет подтверждённые заявки в PostgreSQL.</summary>
public sealed class LeadRepository(BurrfishDbContext db) : ILeadRepository
{
    /// <inheritdoc />
    public Guid Add(StoredConversation conversation, LeadDraft draft)
    {
        var id = Guid.NewGuid();

        db.Leads.Add(new LeadEntity
        {
            Id = id,
            ConversationId = conversation.Id,
            HandoffKey = conversation.Id,
            SenderId = conversation.SenderId,
            Username = conversation.Username,
            Need = draft.Need,
            CreatedAt = draft.CreatedAt,
            NotificationStatus = LeadNotificationStatus.Pending
        });

        return id;
    }
}
