namespace Burrfish.Infrastructure.Configuration;

/// <summary>Настройки периодического обновления агрегированных метрик очередей.</summary>
public sealed class ObservabilityOptions
{
    /// <summary>Имя секции конфигурации.</summary>
    public const string SectionName = "Observability";

    /// <summary>Признак обновления метрик очередей.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>Интервал обновления метрик очередей в секундах.</summary>
    public int QueueRefreshIntervalSeconds { get; init; } = 5;
}
