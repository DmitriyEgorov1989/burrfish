using Burrfish.Infrastructure.Configuration;
using Burrfish.Infrastructure.Observability;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Burrfish.Infrastructure.Processing;

/// <summary>Периодически обновляет агрегированные метрики состояния очередей.</summary>
public sealed class QueueMetricsWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<ObservabilityOptions> options,
    TimeProvider timeProvider,
    WorkerHeartbeat heartbeat,
    ILogger<QueueMetricsWorker> logger) : BackgroundService
{
    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            return;
        }

        var interval = TimeSpan.FromSeconds(options.Value.QueueRefreshIntervalSeconds);
        while (!stoppingToken.IsCancellationRequested)
        {
            heartbeat.Record(WorkerKind.QueueMetrics);
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<QueueMetricsCollector>()
                    .RefreshAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    "Queue metrics refresh failed. ErrorType: {ErrorType}",
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
