namespace Burrfish.Infrastructure.Persistence.Entities;

/// <summary>Состояние обработки входящего события.</summary>
public enum InboxEventStatus
{
    /// <summary>Событие ожидает обработки.</summary>
    Pending,

    /// <summary>Событие обрабатывается worker.</summary>
    Processing,

    /// <summary>Событие успешно обработано.</summary>
    Processed,

    /// <summary>Обработка завершилась окончательной ошибкой.</summary>
    Failed
}
