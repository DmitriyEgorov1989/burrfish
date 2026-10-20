using Burrfish.Application.Instagram;
using Burrfish.Infrastructure.Configuration;
using Burrfish.Infrastructure.Instagram;
using Burrfish.Infrastructure.Persistence;
using Burrfish.Infrastructure.Persistence.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Burrfish.Infrastructure.Processing;

/// <summary>Обрабатывает одну доступную inbox-запись в транзакции.</summary>
public sealed class InboxProcessor(
    BurrfishDbContext db,
    InstagramEventNormalizer normalizer,
    ISender sender,
    TimeProvider timeProvider,
    IOptions<InboxProcessingOptions> options)
{
    /// <summary>Выполняет одну попытку обработки доступного события.</summary>
    /// <param name="cancellationToken">Токен остановки обработки.</param>
    /// <returns><see langword="true"/>, если событие было выбрано; иначе <see langword="false"/>.</returns>
    public async Task<bool> ProcessNextAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        await using var transaction =
            await db.Database.BeginTransactionAsync(cancellationToken);

        var rows = await db.InboxEvents
            .FromSqlInterpolated($"""
                SELECT inbox_events.*, inbox_events.xmin
                FROM inbox_events
                WHERE status = 'Pending'
                  AND next_attempt_at <= {now}
                ORDER BY next_attempt_at, received_at, id
                LIMIT 1
                FOR UPDATE SKIP LOCKED
                """)
            .ToListAsync(cancellationToken);

        var item = rows.SingleOrDefault();
        if (item is null)
        {
            return false;
        }

        item.Status = InboxEventStatus.Processing;
        item.AttemptCount++;
        item.LockedUntil = null;
        item.LastErrorCode = null;
        await db.SaveChangesAsync(cancellationToken);

        var message = normalizer.Normalize(item.Payload);
        if (message is null || message.ExternalEventId != item.ExternalEventId)
        {
            item.Status = InboxEventStatus.Ignored;
            item.LastErrorCode = message is null
                ? "UnsupportedOrMalformedEvent"
                : "EventIdentityMismatch";
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return true;
        }

        const string savepoint = "before_message";
        await transaction.CreateSavepointAsync(savepoint, cancellationToken);

        ProcessIncomingMessageResult result;
        try
        {
            result = await sender.Send(
                new ProcessIncomingMessageCommand(message),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            await ScheduleRetryAsync(
                transaction,
                savepoint,
                item,
                exception.GetType().Name,
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return true;
        }

        if (result.Succeeded)
        {
            item.Status = InboxEventStatus.Processed;
            await db.SaveChangesAsync(cancellationToken);
        }
        else
        {
            await ScheduleRetryAsync(
                transaction,
                savepoint,
                item,
                result.ErrorCode ?? "ProcessingRetry",
                cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private async Task ScheduleRetryAsync(
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction,
        string savepoint,
        InboxEvent item,
        string errorCode,
        CancellationToken cancellationToken)
    {
        await transaction.RollbackToSavepointAsync(savepoint, cancellationToken);
        db.ChangeTracker.Clear();

        var nextAttemptAt = timeProvider.GetUtcNow()
            .Add(GetRetryDelay(item.AttemptCount));

        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE inbox_events
            SET status = 'Pending',
                next_attempt_at = {nextAttemptAt},
                locked_until = NULL,
                last_error_code = {errorCode}
            WHERE id = {item.Id}
            """, cancellationToken);
    }

    private TimeSpan GetRetryDelay(int attemptCount)
    {
        var settings = options.Value;
        var exponent = Math.Min(Math.Max(attemptCount - 1, 0), 30);
        var seconds = Math.Min(
            settings.MaxRetryDelaySeconds,
            settings.RetryBaseDelaySeconds * Math.Pow(2, exponent));

        return TimeSpan.FromSeconds(seconds);
    }
}
