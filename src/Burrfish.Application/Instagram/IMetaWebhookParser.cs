using Burrfish.Application.Persistence;

namespace Burrfish.Application.Instagram;

/// <summary>Разбирает webhook после успешной проверки подписи.</summary>
public interface IMetaWebhookParser
{
    /// <summary>Выделяет отдельные события настроенного аккаунта.</summary>
    /// <param name="body">Исходное проверенное тело запроса.</param>
    /// <param name="receivedAt">Момент получения запроса в UTC.</param>
    /// <returns>События для сохранения; пустой список для игнорируемых событий; <see langword="null"/> для некорректного JSON или структуры.</returns>
    IReadOnlyList<InboxEnvelope>? Parse(
        ReadOnlyMemory<byte> body,
        DateTimeOffset receivedAt);
}
