using Burrfish.Application.Persistence;

namespace Burrfish.ApiContract.Tests.Instagram;

internal sealed class RecordingInboxWriter(bool result = true) : IInboxWriter
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
