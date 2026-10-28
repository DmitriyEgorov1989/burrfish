namespace Burrfish.Infrastructure.Configuration;

/// <summary>Настройки периодического удаления данных с истёкшим сроком хранения.</summary>
public sealed class DataRetentionOptions
{
    /// <summary>Имя секции конфигурации.</summary>
    public const string SectionName = "DataRetention";

    /// <summary>Признак запуска фоновой очистки.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>Интервал между запусками очистки в минутах.</summary>
    public int IntervalMinutes { get; init; } = 60;
}
