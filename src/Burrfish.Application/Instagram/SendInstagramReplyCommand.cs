using Burrfish.Application.Processing;
using MediatR;

namespace Burrfish.Application.Instagram;

/// <summary>Команда одной попытки отправки подготовленного ответа Instagram.</summary>
/// <param name="Reply">Подготовленный ответ; не может быть <see langword="null"/>.</param>
public sealed record SendInstagramReplyCommand(InstagramReply Reply) : IRequest<DeliveryResult>;

/// <summary>Передаёт отправку ответа настроенному адаптеру Instagram.</summary>
public sealed class SendInstagramReplyCommandHandler(IInstagramMessageSender sender)
    : IRequestHandler<SendInstagramReplyCommand, DeliveryResult>
{
    /// <inheritdoc />
    public Task<DeliveryResult> Handle(
        SendInstagramReplyCommand request,
        CancellationToken cancellationToken) =>
        sender.SendAsync(request.Reply, cancellationToken);
}
