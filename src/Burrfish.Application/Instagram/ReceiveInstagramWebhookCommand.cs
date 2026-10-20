using MediatR;

namespace Burrfish.Application.Instagram;

/// <summary>Сохраняет события webhook с уже проверенной подписью.</summary>
/// <param name="Body">Проверенные исходные байты запроса.</param>
/// <param name="ReceivedAt">Момент получения запроса в UTC.</param>
public sealed record ReceiveInstagramWebhookCommand(
    ReadOnlyMemory<byte> Body,
    DateTimeOffset ReceivedAt) : IRequest<bool>;
