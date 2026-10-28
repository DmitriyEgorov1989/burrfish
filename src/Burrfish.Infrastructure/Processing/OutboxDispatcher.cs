using System.Text.Json;
using Burrfish.Application.Instagram;
using Burrfish.Application.Processing;
using Burrfish.Application.Telegram;
using Burrfish.Infrastructure.Configuration;
using Burrfish.Infrastructure.Observability;
using Burrfish.Infrastructure.Persistence;
using Burrfish.Infrastructure.Persistence.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Burrfish.Infrastructure.Processing;

/// <summary>Отправляет одно доступное внешнее задание из outbox.</summary>
public sealed class OutboxDispatcher(
    BurrfishDbContext db,
    ISender sender,
    TimeProvider timeProvider,
    IOptions<OutboxProcessingOptions> options,
    BurrfishMetrics? metrics = null)
{
    private const string InstagramReplyType = "instagram.reply.v1";
    private const string TelegramLeadType = "telegram.lead-created.v1";

    /// <summary>Выполняет одну попытку доставки доступного задания.</summary>
    /// <param name="cancellationToken">Токен остановки обработки.</param>
    /// <returns><see langword="true"/>, если задание было выбрано; иначе <see langword="false"/>.</returns>
    public async Task<bool> DispatchNextAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var rows = await db.OutboxMessages
            .FromSqlInterpolated($"""
                SELECT outbox_messages.*, outbox_messages.xmin
                FROM outbox_messages
                WHERE type IN ('instagram.reply.v1', 'telegram.lead-created.v1')
                  AND status = 'Pending'
                  AND next_attempt_at <= {now}
                ORDER BY next_attempt_at, created_at, id
                LIMIT 1
                FOR UPDATE SKIP LOCKED
                """)
            .ToListAsync(cancellationToken);
        var item = rows.SingleOrDefault();
        if (item is null)
        {
            return false;
        }

        item.Status = OutboxMessageStatus.Processing;
        item.AttemptCount++;
        item.LockedUntil = null;
        item.LastErrorCode = null;
        item.LastErrorMessage = null;
        await db.SaveChangesAsync(cancellationToken);

        if (item.Type == InstagramReplyType)
        {
            await DispatchInstagramAsync(item, cancellationToken);
        }
        else
        {
            await DispatchTelegramAsync(item, cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        if (item.Status == OutboxMessageStatus.Pending)
        {
            metrics?.RecordRetry(item.Type == TelegramLeadType
                ? RetryComponent.Telegram
                : RetryComponent.Instagram);
        }
        return true;
    }

    private async Task DispatchInstagramAsync(
        OutboxMessage item,
        CancellationToken cancellationToken)
    {
        if (!TryReadReply(item.Payload, out var reply))
        {
            Fail(item, "InvalidOutboxPayload", "Instagram reply payload is invalid.");
            return;
        }

        var result = await sender.Send(
            new SendInstagramReplyCommand(reply),
            cancellationToken);
        ApplyResult(item, result, timeProvider.GetUtcNow());
    }

    private async Task DispatchTelegramAsync(
        OutboxMessage item,
        CancellationToken cancellationToken)
    {
        if (!TryReadLeadId(item.Payload, out var leadId))
        {
            Fail(item, "InvalidOutboxPayload", "Telegram lead payload is invalid.");
            return;
        }

        var lead = await db.Leads.SingleOrDefaultAsync(
            entity => entity.Id == leadId,
            cancellationToken);
        if (lead is null)
        {
            Fail(item, "LeadNotFound", "Lead referenced by Telegram job was not found.");
            return;
        }

        if (lead.NotificationStatus == LeadNotificationStatus.Sent)
        {
            item.Status = OutboxMessageStatus.Sent;
            return;
        }

        if (lead.NotificationStatus == LeadNotificationStatus.Failed)
        {
            Fail(item, "LeadNotificationFailed", "Lead notification is already marked as failed.");
            return;
        }

        var result = await sender.Send(
            new SendLeadNotificationCommand(new LeadNotification(
                lead.Id,
                lead.SenderId,
                lead.Username,
                lead.Need,
                lead.CreatedAt)),
            cancellationToken);
        ApplyResult(item, result, timeProvider.GetUtcNow());
        lead.NotificationStatus = result.Status switch
        {
            DeliveryStatus.Sent => LeadNotificationStatus.Sent,
            DeliveryStatus.Failed => LeadNotificationStatus.Failed,
            _ => LeadNotificationStatus.Pending
        };
    }

    private void ApplyResult(OutboxMessage item, DeliveryResult result, DateTimeOffset now)
    {
        item.LastErrorCode = result.ErrorCode;
        item.LastErrorMessage = result.ErrorMessage;
        item.Status = result.Status switch
        {
            DeliveryStatus.Sent => OutboxMessageStatus.Sent,
            DeliveryStatus.Failed => OutboxMessageStatus.Failed,
            _ => OutboxMessageStatus.Pending
        };

        if (result.Status == DeliveryStatus.Retry)
        {
            var delay = GetRetryDelay(item.AttemptCount);
            if (result.RetryAfter is { } retryAfter && retryAfter > delay)
            {
                delay = retryAfter;
            }
            item.NextAttemptAt = now.Add(delay);
        }
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

    private static void Fail(OutboxMessage item, string code, string message)
    {
        item.Status = OutboxMessageStatus.Failed;
        item.LastErrorCode = code;
        item.LastErrorMessage = message;
    }

    private static bool TryReadLeadId(string payload, out Guid leadId)
    {
        leadId = Guid.Empty;
        try
        {
            using var document = JsonDocument.Parse(payload);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("LeadId", out var property)
                && property.ValueKind == JsonValueKind.String
                && property.TryGetGuid(out leadId)
                && leadId != Guid.Empty;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryReadReply(string payload, out InstagramReply reply)
    {
        reply = null!;
        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !TryString(root, "recipientId", out var recipientId)
                || !TryString(root, "text", out var text))
            {
                return false;
            }

            reply = new(recipientId, text);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryString(JsonElement element, string name, out string value)
    {
        value = string.Empty;
        return element.TryGetProperty(name, out var property)
            && property.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(value = property.GetString() ?? string.Empty);
    }
}
