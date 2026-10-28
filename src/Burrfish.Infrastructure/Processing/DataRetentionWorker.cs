using Burrfish.Infrastructure.Configuration;
using Burrfish.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Burrfish.Infrastructure.Processing;

/// <summary>Периодически удаляет данные, достигшие границы срока хранения.</summary>
public sealed class DataRetentionWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<DataRetentionOptions> options,
    TimeProvider timeProvider,
    WorkerHeartbeat heartbeat,
    ILogger<DataRetentionWorker> logger) : BackgroundService
{
    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            return;
        }

        var interval = TimeSpan.FromMinutes(options.Value.IntervalMinutes);
        while (!stoppingToken.IsCancellationRequested)
        {
            heartbeat.Record(WorkerKind.Retention);
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var result = await scope.ServiceProvider
                    .GetRequiredService<DataRetentionService>()
                    .DeleteExpiredAsync(stoppingToken);
                logger.LogInformation(
                    "Data retention completed. Inbox: {InboxCount}, Outbox: {OutboxCount}, Conversations: {ConversationCount}, Leads: {LeadCount}",
                    result.DeletedInboxEvents,
                    result.DeletedOutboxMessages,
                    result.DeletedConversations,
                    result.DeletedLeads);
                if (result.DeletedPendingOutboxMessages > 0)
                {
                    logger.LogWarning(
                        "Data retention removed undelivered jobs. ErrorCode: {ErrorCode}, Count: {Count}",
                        "RetentionExpired",
                        result.DeletedPendingOutboxMessages);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    "Data retention failed. ErrorType: {ErrorType}",
                    exception.GetType().Name);
            }

            try
            {
                await Task.Delay(interval, timeProvider, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
        }
    }
}
