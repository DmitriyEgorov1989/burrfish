using Burrfish.Application.Instagram;
using Burrfish.Application.Persistence;

namespace Burrfish.Application.Tests.Instagram;

public sealed class ReceiveInstagramWebhookCommandHandlerTests
{
    [Fact]
    public async Task Invalid_payload_is_rejected_without_writes()
    {
        var writer = new RecordingInboxWriter();
        var handler = new ReceiveInstagramWebhookCommandHandler(
            new StubParser(null),
            writer);

        var accepted = await handler.Handle(
            CreateCommand(),
            CancellationToken.None);

        Assert.False(accepted);
        Assert.Empty(writer.Envelopes);
    }

    [Fact]
    public async Task Valid_batch_is_fully_offered_to_inbox_writer()
    {
        var receivedAt = new DateTimeOffset(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);
        InboxEnvelope[] envelopes =
        [
            new("mid-1", "{\"message\":1}", receivedAt),
            new("mid-2", "{\"message\":2}", receivedAt)
        ];
        var writer = new RecordingInboxWriter(result: false);
        var handler = new ReceiveInstagramWebhookCommandHandler(
            new StubParser(envelopes),
            writer);

        var accepted = await handler.Handle(
            CreateCommand(),
            CancellationToken.None);

        Assert.True(accepted);
        Assert.Equal(envelopes, writer.Envelopes);
    }

    private static ReceiveInstagramWebhookCommand CreateCommand() => new(
        "{}"u8.ToArray(),
        new DateTimeOffset(2026, 10, 18, 12, 0, 0, TimeSpan.Zero));

    private sealed class StubParser(IReadOnlyList<InboxEnvelope>? result)
        : IMetaWebhookParser
    {
        public IReadOnlyList<InboxEnvelope>? Parse(
            ReadOnlyMemory<byte> body,
            DateTimeOffset receivedAt) => result;
    }

    private sealed class RecordingInboxWriter(bool result = true) : IInboxWriter
    {
        public List<InboxEnvelope> Envelopes { get; } = [];

        public Task<bool> TryStoreAsync(
            InboxEnvelope envelope,
            CancellationToken cancellationToken)
        {
            Envelopes.Add(envelope);
            return Task.FromResult(result);
        }
    }
}
