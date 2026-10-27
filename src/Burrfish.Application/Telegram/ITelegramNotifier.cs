using Burrfish.Application.Processing;

namespace Burrfish.Application.Telegram;

/// <summary>Отправляет уведомления о подтверждённых заявках.</summary>
public interface ITelegramNotifier
{
    /// <summary>Выполняет одну попытку отправки уведомления.</summary>
    /// <param name="notification">Подтверждённая заявка; не может быть <see langword="null"/>.</param>
    /// <param name="cancellationToken">Токен отмены операции.</param>
    /// <returns>Результат доставки либо ожидаемая интеграционная ошибка.</returns>
    Task<DeliveryResult> SendLeadAsync(
        LeadNotification notification,
        CancellationToken cancellationToken);
}
