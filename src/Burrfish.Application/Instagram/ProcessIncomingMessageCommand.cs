using MediatR;

namespace Burrfish.Application.Instagram;

/// <summary>Запускает прикладную обработку входящего сообщения.</summary>
/// <param name="Message">Нормализованное сообщение; не может быть <see langword="null"/>.</param>
public sealed record ProcessIncomingMessageCommand(IncomingInstagramMessage Message)
    : IRequest<ProcessIncomingMessageResult>;
