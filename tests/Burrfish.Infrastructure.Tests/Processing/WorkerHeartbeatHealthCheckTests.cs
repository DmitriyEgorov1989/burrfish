using Burrfish.Infrastructure.Configuration;
using Burrfish.Infrastructure.Processing;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Burrfish.Infrastructure.Tests.Processing;

public sealed class WorkerHeartbeatHealthCheckTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Disabled_workers_do_not_require_heartbeats()
    {
        var check = CreateCheck(
            new TestTimeProvider(), inbox: false, outbox: false, retention: false, queueMetrics: false);

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public async Task Enabled_worker_without_heartbeat_is_unhealthy()
    {
        var check = CreateCheck(
            new TestTimeProvider(), inbox: true, outbox: false, retention: false, queueMetrics: false);

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }

    [Fact]
    public async Task Enabled_workers_with_fresh_heartbeats_are_healthy()
    {
        var time = new TestTimeProvider();
        var heartbeat = new WorkerHeartbeat(time);
        heartbeat.Record(WorkerKind.Inbox);
        heartbeat.Record(WorkerKind.Outbox);
        heartbeat.Record(WorkerKind.Retention);
        heartbeat.Record(WorkerKind.QueueMetrics);
        time.UtcNow = Now.AddSeconds(30);
        var check = CreateCheck(time, true, true, true, true, heartbeat);

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public async Task Stale_heartbeat_is_unhealthy()
    {
        var time = new TestTimeProvider();
        var heartbeat = new WorkerHeartbeat(time);
        heartbeat.Record(WorkerKind.Inbox);
        time.UtcNow = Now.AddSeconds(121);
        var check = CreateCheck(time, true, false, false, false, heartbeat);

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }

    private static WorkerHeartbeatHealthCheck CreateCheck(
        TestTimeProvider time,
        bool inbox,
        bool outbox,
        bool retention,
        bool queueMetrics,
        WorkerHeartbeat? heartbeat = null) => new(
            heartbeat ?? new WorkerHeartbeat(time),
            Options.Create(new InboxProcessingOptions { Enabled = inbox }),
            Options.Create(new OutboxProcessingOptions { Enabled = outbox }),
            Options.Create(new DataRetentionOptions { Enabled = retention }),
            Options.Create(new ObservabilityOptions { Enabled = queueMetrics }));

    private sealed class TestTimeProvider : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = Now;
        public override DateTimeOffset GetUtcNow() => UtcNow;
    }
}
