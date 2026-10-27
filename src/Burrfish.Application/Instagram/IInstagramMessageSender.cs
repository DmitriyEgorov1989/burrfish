using Burrfish.Application.Processing;

namespace Burrfish.Application.Instagram;

/// <summary>Отправляет подготовленные ответы в Instagram.</summary>
public interface IInstagramMessageSender
{
    /// <summary>Выполняет одну попытку отправки без внутренних повторов.</summary>
    /// <param name="reply">Подготовленный ответ; не может быть <see langword="null"/>.</param>
    /// <param name="cancellationToken">Токен отмены операции.</param>
    /// <returns>Результат доставки или ожидаемая интеграционная ошибка.</returns>
    Task<DeliveryResult> SendAsync(InstagramReply reply, CancellationToken cancellationToken);
}
