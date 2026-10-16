namespace Burrfish.Infrastructure.Persistence.Entities;

/// <summary>Состояние доставки исходящего задания.</summary>
public enum OutboxMessageStatus
{
    /// <summary>Задание ожидает отправки.</summary>
    Pending,

    /// <summary>Задание обрабатывается worker.</summary>
    Processing,

    /// <summary>Задание успешно отправлено.</summary>
    Sent,

    /// <summary>Доставка завершилась окончательной ошибкой.</summary>
    Failed
}
