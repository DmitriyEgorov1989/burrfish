namespace Burrfish.Application.Telegram;

/// <summary>Данные подтверждённой заявки для уведомления менеджеров.</summary>
/// <param name="LeadId">Внутренний идентификатор заявки.</param>
/// <param name="SenderId">Instagram ID клиента; не может быть <see langword="null"/>.</param>
/// <param name="Username">Имя пользователя; <see langword="null"/>, если Meta его не предоставила.</param>
/// <param name="Need">Подтверждённая потребность; не может быть <see langword="null"/>.</param>
/// <param name="CreatedAt">Момент подтверждения заявки.</param>
public sealed record LeadNotification(
    Guid LeadId,
    string SenderId,
    string? Username,
    string Need,
    DateTimeOffset CreatedAt);
