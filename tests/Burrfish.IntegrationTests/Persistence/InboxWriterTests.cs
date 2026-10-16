using Burrfish.Application.Persistence;
using Burrfish.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Burrfish.IntegrationTests.Persistence;

[Collection(PostgresCollection.Name)]
public sealed class InboxWriterTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Duplicate_event_is_stored_once()
    {
        var externalId = Guid.NewGuid().ToString("N");
        var receivedAt = new DateTimeOffset(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);
        var envelope = new InboxEnvelope(externalId, "{}", receivedAt);

        await using var firstDb = fixture.CreateContext();
        await using var secondDb = fixture.CreateContext();

        var first = await new InboxWriter(firstDb).TryStoreAsync(envelope, CancellationToken.None);
        var second = await new InboxWriter(secondDb).TryStoreAsync(
            envelope with
            {
                Payload = "[]",
                ReceivedAt = receivedAt.AddMinutes(1)
            },
            CancellationToken.None);

        Assert.True(first);
        Assert.False(second);

        await using var verificationDb = fixture.CreateContext();
        var stored = await verificationDb.InboxEvents.SingleAsync(
            entity => entity.ExternalEventId == externalId);

        Assert.Equal("{}", stored.Payload);
        Assert.Equal(receivedAt, stored.ReceivedAt);
    }

    [Fact]
    public async Task Concurrent_delivery_stores_exactly_one_event()
    {
        var externalId = Guid.NewGuid().ToString("N");
        var envelope = new InboxEnvelope(
            externalId,
            "{}",
            new DateTimeOffset(2026, 10, 16, 12, 0, 0, TimeSpan.Zero));
        var start = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var attempts = Enumerable.Range(0, 8)
            .Select(async _ =>
            {
                await using var db = fixture.CreateContext();
                await start.Task;

                return await new InboxWriter(db).TryStoreAsync(
                    envelope,
                    CancellationToken.None);
            })
            .ToArray();

        start.SetResult(true);
        var results = await Task.WhenAll(attempts);

        Assert.Equal(1, results.Count(result => result));

        await using var verificationDb = fixture.CreateContext();
        Assert.Equal(
            1,
            await verificationDb.InboxEvents.CountAsync(
                entity => entity.ExternalEventId == externalId));
    }
}
