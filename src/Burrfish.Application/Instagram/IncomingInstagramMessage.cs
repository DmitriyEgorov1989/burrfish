namespace Burrfish.Application.Instagram;

/// <summary>Нормализованное входящее текстовое сообщение Instagram.</summary>
/// <param name="ExternalEventId">Уникальный message.mid; не может быть <see langword="null"/>.</param>
/// <param name="ConversationId">Стабильный ключ аккаунта и клиента; не может быть <see langword="null"/>.</param>
/// <param name="SenderId">Instagram ID клиента; не может быть <see langword="null"/>.</param>
/// <param name="Username">Имя пользователя; <see langword="null"/>, если оно неизвестно.</param>
/// <param name="Text">Исходный текст сообщения; не может быть <see langword="null"/>.</param>
/// <param name="ReceivedAt">Время сообщения Meta в UTC.</param>
public sealed record IncomingInstagramMessage(
    string ExternalEventId,
    string ConversationId,
    string SenderId,
    string? Username,
    string Text,
    DateTimeOffset ReceivedAt);
