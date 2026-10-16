using Burrfish.Domain.Conversations;

namespace Burrfish.Infrastructure.Persistence.Entities;

/// <summary>Инфраструктурная оболочка сохраняемого агрегата диалога.</summary>
public sealed class ConversationEntity
{
    /// <summary>Идентификатор экземпляра диалога.</summary>
    public Guid Id { get; set; }

    /// <summary>Внешний идентификатор диалога; не может быть <see langword="null"/>.</summary>
    public string ExternalConversationId { get; set; } = string.Empty;

    /// <summary>Instagram ID клиента; не может быть <see langword="null"/>.</summary>
    public string SenderId { get; set; } = string.Empty;

    /// <summary>Имя пользователя; <see langword="null"/>, если оно неизвестно.</summary>
    public string? Username { get; set; }

    /// <summary>Момент создания экземпляра диалога в UTC.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Отслеживаемый доменный агрегат; не может быть <see langword="null"/>.</summary>
    public Conversation Model { get; set; } = null!;

    /// <summary>Версия строки PostgreSQL для оптимистичной конкуренции.</summary>
    public uint Version { get; set; }
}
