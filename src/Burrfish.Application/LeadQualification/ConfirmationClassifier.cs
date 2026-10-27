namespace Burrfish.Application.LeadQualification;

/// <summary>Результат распознавания ответа на предложение передачи.</summary>
public enum ConfirmationKind
{
    /// <summary>Ответ не выражает однозначного решения.</summary>
    Ambiguous,

    /// <summary>Клиент явно согласился.</summary>
    Confirmed,

    /// <summary>Клиент явно отказался.</summary>
    Declined
}

/// <summary>Распознаёт явное согласие и отказ без обращения к ИИ.</summary>
public static class ConfirmationClassifier
{
    /// <summary>Классифицирует полный ответ, не используя поиск подстрок.</summary>
    /// <param name="text">Ответ; <see langword="null"/> считается неоднозначным.</param>
    /// <returns>Согласие, отказ либо неоднозначный ответ.</returns>
    public static ConfirmationKind Classify(string? text)
    {
        var normalized = text?.Trim().TrimEnd('.', '!', '?').Trim().ToLowerInvariant();

        return normalized switch
        {
            "да" or "согласен" or "согласна" or "передайте" or "да, передайте"
                => ConfirmationKind.Confirmed,
            "нет" or "не надо" or "не нужно" or "не передавайте" or "нет, спасибо"
                => ConfirmationKind.Declined,
            _ => ConfirmationKind.Ambiguous
        };
    }
}
