using Burrfish.Application.Persistence;
using MediatR;

namespace Burrfish.Application.Instagram;

/// <summary>Сохраняет отдельные события проверенного webhook.</summary>
public sealed class ReceiveInstagramWebhookCommandHandler(
    IMetaWebhookParser parser,
    IInboxWriter inboxWriter)
    : IRequestHandler<ReceiveInstagramWebhookCommand, bool>
{
    /// <inheritdoc />
    public async Task<bool> Handle(
        ReceiveInstagramWebhookCommand request,
        CancellationToken cancellationToken)
    {
        var envelopes = parser.Parse(request.Body, request.ReceivedAt);
        if (envelopes is null)
        {
            return false;
        }

        foreach (var envelope in envelopes)
        {
            await inboxWriter.TryStoreAsync(envelope, cancellationToken);
        }

        return true;
    }
}
