namespace Burrfish.Application.OpenAI;

/// <summary>Ошибка обращения к OpenAI или проверки ответа.</summary>
/// <param name="Code">Исходный код API либо локальный технический код; не может быть <see langword="null"/>.</param>
/// <param name="Message">Исходное сообщение API либо локальное описание; не может быть <see langword="null"/>.</param>
/// <param name="HttpStatusCode">HTTP-статус; <see langword="null"/>, если ответ не получен.</param>
/// <param name="IsTransient">Признак временной ошибки.</param>
public sealed record AiDialogError(
    string Code,
    string Message,
    int? HttpStatusCode,
    bool IsTransient);

/// <summary>Решение модели либо ожидаемая ошибка интеграции.</summary>
public sealed record AiDialogResult
{
    private AiDialogResult(AiDialogDecision? decision, AiDialogError? error)
    {
        Decision = decision;
        Error = error;
    }

    /// <summary>Проверенное решение; <see langword="null"/> при ошибке.</summary>
    public AiDialogDecision? Decision { get; }

    /// <summary>Ошибка; <see langword="null"/> при успехе.</summary>
    public AiDialogError? Error { get; }

    /// <summary>Признак успешного получения решения.</summary>
    public bool Succeeded => Decision is not null;

    /// <summary>Создаёт успешный результат.</summary>
    /// <param name="decision">Проверенное решение; не может быть <see langword="null"/>.</param>
    /// <returns>Успешный результат.</returns>
    public static AiDialogResult Success(AiDialogDecision decision) =>
        new(decision, null);

    /// <summary>Создаёт результат ошибки.</summary>
    /// <param name="error">Описание ошибки; не может быть <see langword="null"/>.</param>
    /// <returns>Результат ошибки.</returns>
    public static AiDialogResult Failure(AiDialogError error) =>
        new(null, error);
}
