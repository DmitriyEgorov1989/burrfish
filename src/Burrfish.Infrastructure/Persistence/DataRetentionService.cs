using Burrfish.Infrastructure.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Burrfish.Infrastructure.Persistence;

/// <summary>Удаляет клиентские данные после окончания установленного срока хранения.</summary>
public sealed class DataRetentionService(
    BurrfishDbContext db,
    TimeProvider timeProvider,
    IOptions<DialogOptions> options)
{
    /// <summary>Удаляет записи, достигшие границы срока хранения, одной транзакцией.</summary>
    /// <param name="cancellationToken">Токен отмены операции.</param>
    /// <returns>Граница срока хранения и количество удалённых записей каждого типа.</returns>
    public async Task<DataRetentionResult> DeleteExpiredAsync(
        CancellationToken cancellationToken)
    {
        var cutoff = timeProvider.GetUtcNow().AddDays(-options.Value.RetentionDays);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var deletedPendingOutboxMessages = await db.OutboxMessages.LongCountAsync(
            item => item.CreatedAt <= cutoff
                && item.Status == Entities.OutboxMessageStatus.Pending,
            cancellationToken);
        var deletedOutboxMessages = await db.OutboxMessages
            .Where(item => item.CreatedAt <= cutoff)
            .ExecuteDeleteAsync(cancellationToken);
        var deletedInboxEvents = await db.InboxEvents
            .Where(item => item.ReceivedAt <= cutoff)
            .ExecuteDeleteAsync(cancellationToken);
        var deletedLeads = await db.Leads
            .Where(item => item.CreatedAt <= cutoff)
            .ExecuteDeleteAsync(cancellationToken);
        var deletedConversations = await db.Conversations
            .Where(item => item.CreatedAt <= cutoff)
            .ExecuteDeleteAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return new(
            cutoff,
            deletedInboxEvents,
            deletedOutboxMessages,
            deletedConversations,
            deletedLeads,
            deletedPendingOutboxMessages);
    }
}

/// <summary>Агрегированный результат очистки без клиентских данных и идентификаторов.</summary>
/// <param name="Cutoff">Граница хранения в UTC; записи на этой границе также удалены.</param>
/// <param name="DeletedInboxEvents">Количество удалённых входящих событий.</param>
/// <param name="DeletedOutboxMessages">Количество удалённых исходящих заданий.</param>
/// <param name="DeletedConversations">Количество удалённых диалогов.</param>
/// <param name="DeletedLeads">Количество удалённых заявок.</param>
/// <param name="DeletedPendingOutboxMessages">Количество удалённых заданий, которые ещё ожидали доставки.</param>
public sealed record DataRetentionResult(
    DateTimeOffset Cutoff,
    int DeletedInboxEvents,
    int DeletedOutboxMessages,
    int DeletedConversations,
    int DeletedLeads,
    long DeletedPendingOutboxMessages);
