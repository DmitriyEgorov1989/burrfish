namespace Burrfish.Infrastructure.Persistence.Entities;

/// <summary>Сохранённое входящее событие внешней платформы.</summary>
public sealed class InboxEvent
{
    /// <summary>Внутренний идентификатор события.</summary>
    public Guid Id { get; set; }

    /// <summary>Уникальный внешний идентификатор; не может быть <see langword="null"/>.</summary>
    public string ExternalEventId { get; set; } = string.Empty;

    /// <summary>Исходный JSON; не может быть <see langword="null"/>.</summary>
    public string Payload { get; set; } = string.Empty;

    /// <summary>Момент получения события в UTC.</summary>
    public DateTimeOffset ReceivedAt { get; set; }

    /// <summary>Текущее состояние обработки.</summary>
    public InboxEventStatus Status { get; set; }

    /// <summary>Количество завершённых попыток обработки.</summary>
    public int AttemptCount { get; set; }

    /// <summary>Первый момент следующей допустимой попытки в UTC.</summary>
    public DateTimeOffset NextAttemptAt { get; set; }

    /// <summary>Срок захвата worker; <see langword="null"/>, если событие не захвачено.</summary>
    public DateTimeOffset? LockedUntil { get; set; }

    /// <summary>Код последней ошибки; <see langword="null"/>, если ошибки нет.</summary>
    public string? LastErrorCode { get; set; }

    /// <summary>Версия строки PostgreSQL для оптимистичной конкуренции.</summary>
    public uint Version { get; set; }
}
