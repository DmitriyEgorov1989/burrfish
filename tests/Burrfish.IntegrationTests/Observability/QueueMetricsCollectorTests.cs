using System.Diagnostics.Metrics;
using Burrfish.Infrastructure.Observability;
using Burrfish.Infrastructure.Persistence.Entities;
using Burrfish.IntegrationTests.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Burrfish.IntegrationTests.Observability;

[Collection(PostgresCollection.Name)]
public sealed class QueueMetricsCollectorTests(PostgresFixture fixture)
{
    private static readonly DateTimeOffset Now =
        new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RefreshAsync_publishes_counts_and_oldest_pending_age()
    {
        await using (var db = fixture.CreateContext())
        {
            await db.OutboxMessages.ExecuteDeleteAsync();
            await db.InboxEvents.ExecuteDeleteAsync();
            db.InboxEvents.Add(CreateInbox(Now.AddSeconds(-75)));
            db.OutboxMessages.AddRange(
                CreateOutbox("instagram.reply.v1", OutboxMessageStatus.Pending, Now.AddSeconds(-40)),
                CreateOutbox("instagram.reply.v1", OutboxMessageStatus.Failed, Now.AddSeconds(-100)),
                CreateOutbox("telegram.lead-created.v1", OutboxMessageStatus.Pending, Now.AddSeconds(-10)));
            await db.SaveChangesAsync();
        }

        var measurements = new List<(string Name, double Value, string Tags)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, current) =>
        {
            if (instrument.Meter.Name == BurrfishMetrics.MeterName)
            {
                current.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            measurements.Add((instrument.Name, value, FormatTags(tags))));
        listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
            measurements.Add((instrument.Name, value, FormatTags(tags))));
        listener.Start();
        using var metrics = new BurrfishMetrics();
        await using (var db = fixture.CreateContext())
        {
            await new QueueMetricsCollector(db, metrics, new FixedTimeProvider())
                .RefreshAsync(CancellationToken.None);
        }

        listener.RecordObservableInstruments();

        Assert.Contains(measurements, x =>
            x.Name == "burrfish.outbox.deliveries" && x.Value == 1
            && x.Tags == "destination=instagram,status=pending");
        Assert.Contains(measurements, x =>
            x.Name == "burrfish.outbox.deliveries" && x.Value == 1
            && x.Tags == "destination=instagram,status=failed");
        Assert.Contains(measurements, x =>
            x.Name == "burrfish.queue.oldest.age" && x.Value == 75
            && x.Tags == "queue=inbox");
        Assert.Contains(measurements, x =>
            x.Name == "burrfish.queue.oldest.age" && x.Value == 40
            && x.Tags == "queue=instagram");
        Assert.Contains(measurements, x =>
            x.Name == "burrfish.queue.oldest.age" && x.Value == 10
            && x.Tags == "queue=telegram");
    }

    private static InboxEvent CreateInbox(DateTimeOffset receivedAt) => new()
    {
        Id = Guid.NewGuid(),
        ExternalEventId = Guid.NewGuid().ToString("N"),
        Payload = "{}",
        ReceivedAt = receivedAt,
        Status = InboxEventStatus.Pending,
        NextAttemptAt = receivedAt
    };

    private static OutboxMessage CreateOutbox(
        string type,
        OutboxMessageStatus status,
        DateTimeOffset createdAt) => new()
    {
        Id = Guid.NewGuid(),
        DeduplicationKey = Guid.NewGuid().ToString("N"),
        Type = type,
        Payload = "{}",
        CreatedAt = createdAt,
        Status = status,
        NextAttemptAt = createdAt
    };

    private static string FormatTags(ReadOnlySpan<KeyValuePair<string, object?>> tags) =>
        string.Join(",", tags.ToArray().Select(tag => $"{tag.Key}={tag.Value}"));

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
