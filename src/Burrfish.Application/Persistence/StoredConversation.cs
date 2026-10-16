using Burrfish.Domain.Conversations;

namespace Burrfish.Application.Persistence;

/// <summary>Диалог вместе с идентификаторами и контактными данными хранения.</summary>
/// <param name="Id">Идентификатор экземпляра диалога.</param>
/// <param name="ExternalConversationId">Внешний идентификатор; не может быть <see langword="null"/>.</param>
/// <param name="SenderId">Instagram ID клиента; не может быть <see langword="null"/>.</param>
/// <param name="Username">Имя пользователя; <see langword="null"/>, если оно неизвестно.</param>
/// <param name="CreatedAt">Момент создания экземпляра диалога в UTC.</param>
/// <param name="Conversation">Доменный агрегат; не может быть <see langword="null"/>.</param>
public sealed record StoredConversation(
    Guid Id,
    string ExternalConversationId,
    string SenderId,
    string? Username,
    DateTimeOffset CreatedAt,
    Conversation Conversation);
