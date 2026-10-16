using Burrfish.Application.Persistence;
using Burrfish.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Burrfish.Infrastructure.Persistence;

/// <summary>Хранит агрегаты диалогов в PostgreSQL.</summary>
public sealed class ConversationRepository(BurrfishDbContext db) : IConversationRepository
{
    /// <inheritdoc />
    public async Task<StoredConversation?> FindAsync(
        string externalConversationId,
        CancellationToken cancellationToken)
    {
        var entity = await db.Conversations.SingleOrDefaultAsync(
            conversation => conversation.ExternalConversationId == externalConversationId,
            cancellationToken);

        return entity is null
            ? null
            : new StoredConversation(
                entity.Id,
                entity.ExternalConversationId,
                entity.SenderId,
                entity.Username,
                entity.CreatedAt,
                entity.Model);
    }

    /// <inheritdoc />
    public void Add(StoredConversation conversation)
    {
        db.Conversations.Add(new ConversationEntity
        {
            Id = conversation.Id,
            ExternalConversationId = conversation.ExternalConversationId,
            SenderId = conversation.SenderId,
            Username = conversation.Username,
            CreatedAt = conversation.CreatedAt.ToUniversalTime(),
            Model = conversation.Conversation
        });
    }
}
