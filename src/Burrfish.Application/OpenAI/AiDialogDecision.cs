namespace Burrfish.Application.OpenAI;

/// <summary>Разрешённое действие по результату анализа сообщения.</summary>
public enum AiDialogDecisionKind
{
    /// <summary>Выбрать утверждённый шаблон о цене.</summary>
    Price,

    /// <summary>Выбрать утверждённый шаблон об услугах.</summary>
    Services,

    /// <summary>Задать уточняющий вопрос.</summary>
    AskClarifyingQuestion,

    /// <summary>Предложить передачу сформулированной потребности.</summary>
    ProposeHandoff
}

/// <summary>Проверенное структурированное решение модели.</summary>
/// <param name="Kind">Разрешённый тип действия.</param>
/// <param name="Text">
/// Уточняющий вопрос или потребность; <see langword="null"/> для шаблонных ответов.
/// </param>
public sealed record AiDialogDecision(
    AiDialogDecisionKind Kind,
    string? Text);
