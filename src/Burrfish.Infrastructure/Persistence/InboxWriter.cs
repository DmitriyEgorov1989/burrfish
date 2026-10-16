using Burrfish.Application.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Burrfish.Infrastructure.Persistence;

/// <summary>Сохраняет входящие события средствами PostgreSQL.</summary>
public sealed class InboxWriter(BurrfishDbContext db) : IInboxWriter
{
    /// <inheritdoc />
    public async Task<bool> TryStoreAsync(
        InboxEnvelope envelope,
        CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid();
        var receivedAt = envelope.ReceivedAt.ToUniversalTime();

        var affected = await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO inbox_events
                (id, external_event_id, payload, received_at, status, attempt_count, next_attempt_at)
            VALUES
                ({id}, {envelope.ExternalEventId}, {envelope.Payload}, {receivedAt}, 'Pending', 0, {receivedAt})
            ON CONFLICT (external_event_id) DO NOTHING
            """,
            cancellationToken);

        return affected == 1;
    }
}
