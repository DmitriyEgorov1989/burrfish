namespace Burrfish.Application.Processing;

/// <summary>Решение о состоянии исходящего задания после одной попытки.</summary>
public enum DeliveryStatus
{
    /// <summary>Внешняя система подтвердила приём сообщения.</summary>
    Sent,

    /// <summary>Доставку следует повторить позднее.</summary>
    Retry,

    /// <summary>Доставка завершилась окончательной ошибкой.</summary>
    Failed
}

/// <summary>Результат одной попытки доставки.</summary>
/// <param name="Status">Решение о дальнейшем состоянии задания.</param>
/// <param name="ExternalId">Идентификатор внешнего сообщения; <see langword="null"/>, если сообщение не принято.</param>
/// <param name="ErrorCode">Исходный код ошибки; <see langword="null"/> при успехе.</param>
/// <param name="ErrorMessage">Исходное сообщение ошибки; <see langword="null"/> при успехе.</param>
/// <param name="RetryAfter">Минимальная задержка повтора; <see langword="null"/>, если внешняя система её не задала.</param>
public sealed record DeliveryResult(
    DeliveryStatus Status,
    string? ExternalId,
    string? ErrorCode,
    string? ErrorMessage,
    TimeSpan? RetryAfter);
