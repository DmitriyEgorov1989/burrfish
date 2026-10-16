using Burrfish.Application.Persistence;
using Burrfish.Infrastructure.Persistence.Entities;

namespace Burrfish.Infrastructure.Persistence;

/// <summary>Добавляет исходящие задания в PostgreSQL outbox.</summary>
public sealed class OutboxWriter(BurrfishDbContext db) : IOutboxWriter
{
    /// <inheritdoc />
    public void Add(OutboxEnvelope envelope)
    {
        var createdAt = envelope.CreatedAt.ToUniversalTime();

        db.OutboxMessages.Add(new OutboxMessage
        {
            Id = envelope.Id,
            DeduplicationKey = envelope.DeduplicationKey,
            Type = envelope.Type,
            Payload = envelope.Payload,
            CreatedAt = createdAt,
            Status = OutboxMessageStatus.Pending,
            AttemptCount = 0,
            NextAttemptAt = createdAt
        });
    }
}
