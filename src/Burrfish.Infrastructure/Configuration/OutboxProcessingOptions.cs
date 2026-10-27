namespace Burrfish.Infrastructure.Configuration;

/// <summary>Настройки фоновой доставки исходящих заданий.</summary>
public sealed class OutboxProcessingOptions
{
    /// <summary>Имя секции конфигурации.</summary>
    public const string SectionName = "OutboxProcessing";

    /// <summary>Признак включения фоновой доставки.</summary>
    public bool Enabled { get; init; }

    /// <summary>Пауза пустого опроса в миллисекундах.</summary>
    public int PollIntervalMilliseconds { get; init; } = 1000;

    /// <summary>Начальная задержка повторной попытки в секундах.</summary>
    public int RetryBaseDelaySeconds { get; init; } = 30;

    /// <summary>Максимальная расчётная задержка повторной попытки в секундах.</summary>
    public int MaxRetryDelaySeconds { get; init; } = 900;
}
