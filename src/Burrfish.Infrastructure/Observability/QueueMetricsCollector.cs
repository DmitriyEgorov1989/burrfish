using Burrfish.Infrastructure.Persistence;
using Burrfish.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Burrfish.Infrastructure.Observability;

/// <summary>Получает агрегированный снимок очередей из PostgreSQL.</summary>
public sealed class QueueMetricsCollector(
    BurrfishDbContext db,
    BurrfishMetrics metrics,
    TimeProvider timeProvider)
{
    /// <summary>Обновляет опубликованный снимок очередей.</summary>
    /// <param name="cancellationToken">Токен отмены операции.</param>
    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        const string instagram = "instagram.reply.v1";
        const string telegram = "telegram.lead-created.v1";
        var now = timeProvider.GetUtcNow();

        var instagramPending = await CountOutboxAsync(instagram, OutboxMessageStatus.Pending, cancellationToken);
        var instagramFailed = await CountOutboxAsync(instagram, OutboxMessageStatus.Failed, cancellationToken);
        var telegramPending = await CountOutboxAsync(telegram, OutboxMessageStatus.Pending, cancellationToken);
        var telegramFailed = await CountOutboxAsync(telegram, OutboxMessageStatus.Failed, cancellationToken);
        var oldestInbox = await db.InboxEvents
            .Where(item => item.Status == InboxEventStatus.Pending)
            .Select(item => (DateTimeOffset?)item.ReceivedAt)
            .MinAsync(cancellationToken);
        var oldestInstagram = await OldestOutboxAsync(instagram, cancellationToken);
        var oldestTelegram = await OldestOutboxAsync(telegram, cancellationToken);

        metrics.UpdateQueueSnapshot(new(
            instagramPending,
            instagramFailed,
            telegramPending,
            telegramFailed,
            AgeSeconds(now, oldestInbox),
            AgeSeconds(now, oldestInstagram),
            AgeSeconds(now, oldestTelegram)));
    }

    private Task<long> CountOutboxAsync(
        string type,
        OutboxMessageStatus status,
        CancellationToken cancellationToken) => db.OutboxMessages.LongCountAsync(
            item => item.Type == type && item.Status == status,
            cancellationToken);

    private Task<DateTimeOffset?> OldestOutboxAsync(
        string type,
        CancellationToken cancellationToken) => db.OutboxMessages
            .Where(item => item.Type == type && item.Status == OutboxMessageStatus.Pending)
            .Select(item => (DateTimeOffset?)item.CreatedAt)
            .MinAsync(cancellationToken);

    private static double AgeSeconds(DateTimeOffset now, DateTimeOffset? createdAt) =>
        createdAt is null ? 0 : Math.Max(0, (now - createdAt.Value).TotalSeconds);
}
