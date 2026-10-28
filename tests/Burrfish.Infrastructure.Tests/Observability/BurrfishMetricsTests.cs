using System.Diagnostics.Metrics;
using Burrfish.Infrastructure.Observability;

namespace Burrfish.Infrastructure.Tests.Observability;

public sealed class BurrfishMetricsTests
{
    [Fact]
    public void Metrics_publish_bounded_technical_values_and_queue_snapshot()
    {
        var measurements = new List<MeasurementRecord>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, current) =>
        {
            if (instrument.Meter.Name == BurrfishMetrics.MeterName)
            {
                current.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            measurements.Add(new(instrument.Name, value, CopyTags(tags))));
        listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
            measurements.Add(new(instrument.Name, value, CopyTags(tags))));
        listener.Start();
        using var metrics = new BurrfishMetrics();

        metrics.RecordEvent(EventOutcome.Accepted);
        metrics.RecordEvent(EventOutcome.Stored);
        metrics.RecordRetry(RetryComponent.Telegram);
        metrics.RecordLeadCreated();
        metrics.RecordMessageDuration(125, succeeded: true);
        metrics.UpdateQueueSnapshot(new(
            2,
            1,
            3,
            4,
            75,
            40,
            10));
        listener.RecordObservableInstruments();

        Assert.Contains(measurements, item => item is
            { Name: "burrfish.events", Value: 1 } && item.Tags["outcome"] == "accepted");
        Assert.Contains(measurements, item => item is
            { Name: "burrfish.retries", Value: 1 } && item.Tags["component"] == "telegram");
        Assert.Contains(measurements, item => item is
            { Name: "burrfish.leads.created", Value: 1 });
        Assert.Contains(measurements, item => item is
            { Name: "burrfish.message.processing.duration", Value: 125 }
            && item.Tags["outcome"] == "processed");
        Assert.Contains(measurements, item => item is
            { Name: "burrfish.outbox.deliveries", Value: 3 }
            && item.Tags["destination"] == "telegram"
            && item.Tags["status"] == "pending");
        Assert.Contains(measurements, item => item is
            { Name: "burrfish.queue.oldest.age", Value: 75 }
            && item.Tags["queue"] == "inbox");
        Assert.All(measurements.SelectMany(item => item.Tags.Keys), tag =>
            Assert.Contains(tag, new[] { "outcome", "component", "destination", "status", "queue" }));
    }

    private static Dictionary<string, string?> CopyTags(
        ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        var result = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var tag in tags)
        {
            result[tag.Key] = tag.Value?.ToString();
        }
        return result;
    }

    private sealed record MeasurementRecord(
        string Name,
        double Value,
        IReadOnlyDictionary<string, string?> Tags);
}
