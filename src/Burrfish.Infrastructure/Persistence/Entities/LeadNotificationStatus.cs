namespace Burrfish.Infrastructure.Persistence.Entities;

/// <summary>Состояние уведомления менеджера о заявке.</summary>
public enum LeadNotificationStatus
{
    /// <summary>Уведомление ожидает отправки.</summary>
    Pending,

    /// <summary>Уведомление успешно отправлено.</summary>
    Sent,

    /// <summary>Отправка завершилась окончательной ошибкой.</summary>
    Failed
}
