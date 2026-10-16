using Burrfish.Application.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Burrfish.Infrastructure.Persistence;

/// <summary>Фиксирует изменения одного сценария диалога.</summary>
public sealed class ConversationPersistence(BurrfishDbContext db) : IConversationPersistence
{
    /// <inheritdoc />
    public async Task<PersistenceCommitResult> CommitAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return new(true, null);
        }
        catch (DbUpdateConcurrencyException)
        {
            return new(false, "ConcurrencyConflict");
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                ConstraintName: "ux_conversations_external_id"
                    or "ux_leads_handoff_key"
                    or "ux_outbox_deduplication_key"
            })
        {
            return new(false, "PersistenceConflict");
        }
    }
}
