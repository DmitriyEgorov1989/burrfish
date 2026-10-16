namespace Burrfish.Infrastructure.Persistence.Entities;

/// <summary>Сохранённая подтверждённая заявка клиента.</summary>
public sealed class LeadEntity
{
    /// <summary>Внутренний идентификатор заявки.</summary>
    public Guid Id { get; set; }

    /// <summary>Идентификатор диалога; <see langword="null"/> после удаления диалога.</summary>
    public Guid? ConversationId { get; set; }

    /// <summary>Неизменяемый ключ подтверждения заявки.</summary>
    public Guid HandoffKey { get; set; }

    /// <summary>Instagram ID клиента; не может быть <see langword="null"/>.</summary>
    public string SenderId { get; set; } = string.Empty;

    /// <summary>Имя пользователя; <see langword="null"/>, если оно неизвестно.</summary>
    public string? Username { get; set; }

    /// <summary>Подтверждённая потребность; не может быть <see langword="null"/>.</summary>
    public string Need { get; set; } = string.Empty;

    /// <summary>Момент создания заявки в UTC.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Состояние уведомления менеджера.</summary>
    public LeadNotificationStatus NotificationStatus { get; set; }

    /// <summary>Версия строки PostgreSQL для оптимистичной конкуренции.</summary>
    public uint Version { get; set; }

    /// <summary>Связанный диалог; <see langword="null"/>, если он удалён.</summary>
    public ConversationEntity? Conversation { get; set; }
}
