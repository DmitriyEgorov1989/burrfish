using System.Collections.Concurrent;
using Burrfish.Infrastructure.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Burrfish.Infrastructure.Processing;

/// <summary>Виды фоновых обработчиков, состояние которых влияет на готовность приложения.</summary>
public enum WorkerKind
{
    /// <summary>Обработчик входящих событий.</summary>
    Inbox,

    /// <summary>Обработчик исходящих заданий.</summary>
    Outbox,

    /// <summary>Обработчик удаления просроченных данных.</summary>
    Retention,

    /// <summary>Обработчик обновления снимка метрик очередей.</summary>
    QueueMetrics
}

/// <summary>Хранит последний сигнал активности фоновых обработчиков в памяти процесса.</summary>
public sealed class WorkerHeartbeat(TimeProvider timeProvider)
{
    private readonly ConcurrentDictionary<WorkerKind, DateTimeOffset> timestamps = new();

    /// <summary>Записывает текущий момент активности обработчика.</summary>
    /// <param name="worker">Вид обработчика.</param>
    public void Record(WorkerKind worker) => timestamps[worker] = timeProvider.GetUtcNow();

    /// <summary>Проверяет наличие свежего сигнала активности.</summary>
    /// <param name="worker">Вид обработчика.</param>
    /// <param name="maximumAge">Максимальный допустимый возраст сигнала.</param>
    /// <returns><see langword="true"/>, если сигнал существует и не устарел.</returns>
    public bool IsFresh(WorkerKind worker, TimeSpan maximumAge)
    {
        if (!timestamps.TryGetValue(worker, out var timestamp))
        {
            return false;
        }

        var age = timeProvider.GetUtcNow() - timestamp;
        return age >= TimeSpan.Zero && age <= maximumAge;
    }
}

/// <summary>Проверяет сигналы активности всех включённых фоновых обработчиков.</summary>
public sealed class WorkerHeartbeatHealthCheck(
    WorkerHeartbeat heartbeat,
    IOptions<InboxProcessingOptions> inboxOptions,
    IOptions<OutboxProcessingOptions> outboxOptions,
    IOptions<DataRetentionOptions> retentionOptions,
    IOptions<ObservabilityOptions> observabilityOptions) : IHealthCheck
{
    private static readonly TimeSpan MinimumProcessingMaximumAge = TimeSpan.FromSeconds(120);

    /// <inheritdoc />
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var inboxFresh = !inboxOptions.Value.Enabled
            || heartbeat.IsFresh(
                WorkerKind.Inbox,
                GetProcessingMaximumAge(inboxOptions.Value.PollIntervalMilliseconds));
        var outboxFresh = !outboxOptions.Value.Enabled
            || heartbeat.IsFresh(
                WorkerKind.Outbox,
                GetProcessingMaximumAge(outboxOptions.Value.PollIntervalMilliseconds));
        var retentionFresh = !retentionOptions.Value.Enabled
            || heartbeat.IsFresh(
                WorkerKind.Retention,
                TimeSpan.FromMinutes(retentionOptions.Value.IntervalMinutes * 2d));
        var queueMetricsFresh = !observabilityOptions.Value.Enabled
            || heartbeat.IsFresh(
                WorkerKind.QueueMetrics,
                GetProcessingMaximumAge(
                    observabilityOptions.Value.QueueRefreshIntervalSeconds * 1_000));

        return Task.FromResult(inboxFresh && outboxFresh && retentionFresh && queueMetricsFresh
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy());
    }

    private static TimeSpan GetProcessingMaximumAge(int pollIntervalMilliseconds) =>
        TimeSpan.FromMilliseconds(Math.Max(
            MinimumProcessingMaximumAge.TotalMilliseconds,
            pollIntervalMilliseconds * 3d));
}
