namespace Burrfish.Infrastructure.Persistence.Entities;

/// <summary>Сохраняемое задание на отправку во внешнюю систему.</summary>
public sealed class OutboxMessage
{
    /// <summary>Внутренний идентификатор задания.</summary>
    public Guid Id { get; set; }

    /// <summary>Уникальный ключ логического задания; не может быть <see langword="null"/>.</summary>
    public string DeduplicationKey { get; set; } = string.Empty;

    /// <summary>Версионированный тип задания; не может быть <see langword="null"/>.</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>JSON задания; не может быть <see langword="null"/>.</summary>
    public string Payload { get; set; } = string.Empty;

    /// <summary>Момент создания задания в UTC.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Текущее состояние доставки.</summary>
    public OutboxMessageStatus Status { get; set; }

    /// <summary>Количество завершённых попыток отправки.</summary>
    public int AttemptCount { get; set; }

    /// <summary>Первый момент следующей допустимой попытки в UTC.</summary>
    public DateTimeOffset NextAttemptAt { get; set; }

    /// <summary>Срок захвата worker; <see langword="null"/>, если задание не захвачено.</summary>
    public DateTimeOffset? LockedUntil { get; set; }

    /// <summary>Код последней ошибки; <see langword="null"/>, если ошибки нет.</summary>
    public string? LastErrorCode { get; set; }

    /// <summary>Сообщение последней ошибки; <see langword="null"/>, если ошибки нет.</summary>
    public string? LastErrorMessage { get; set; }

    /// <summary>Версия строки PostgreSQL для оптимистичной конкуренции.</summary>
    public uint Version { get; set; }
}
