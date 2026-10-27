using Burrfish.Application.Processing;
using MediatR;

namespace Burrfish.Application.Telegram;

/// <summary>Команда одной попытки уведомления менеджеров о заявке.</summary>
/// <param name="Notification">Данные заявки; не могут быть <see langword="null"/>.</param>
public sealed record SendLeadNotificationCommand(LeadNotification Notification)
    : IRequest<DeliveryResult>;

/// <summary>Передаёт уведомление настроенному адаптеру Telegram.</summary>
public sealed class SendLeadNotificationCommandHandler(ITelegramNotifier notifier)
    : IRequestHandler<SendLeadNotificationCommand, DeliveryResult>
{
    /// <inheritdoc />
    public Task<DeliveryResult> Handle(
        SendLeadNotificationCommand request,
        CancellationToken cancellationToken) =>
        notifier.SendLeadAsync(request.Notification, cancellationToken);
}
