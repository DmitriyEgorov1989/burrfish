namespace Burrfish.Infrastructure.Configuration;

/// <summary>Настройки сценариев русскоязычного диалога.</summary>
public sealed class DialogOptions
{
    /// <summary>Имя секции конфигурации.</summary>
    public const string SectionName = "Dialog";

    /// <summary>Срок хранения сообщений и заявок в календарных днях.</summary>
    public int RetentionDays { get; init; } = 30;

    /// <summary>Примеры фраз о цене; массив не может быть <see langword="null"/>.</summary>
    public string[] PriceExamples { get; init; } = [];

    /// <summary>Примеры фраз об услугах; массив не может быть <see langword="null"/>.</summary>
    public string[] ServicesExamples { get; init; } = [];

    /// <summary>Утверждённый ответ на вопрос о цене; не может быть <see langword="null"/>.</summary>
    public string PriceResponse { get; init; } = string.Empty;

    /// <summary>Утверждённый ответ на вопрос об услугах; не может быть <see langword="null"/>.</summary>
    public string ServicesResponse { get; init; } = string.Empty;

    /// <summary>Резервный ответ при невозможности уточнить потребность; не может быть <see langword="null"/>.</summary>
    public string FallbackResponse { get; init; } = string.Empty;

    /// <summary>Предложение передать обращение менеджеру; не может быть <see langword="null"/>.</summary>
    public string HandoffOffer { get; init; } = string.Empty;

    /// <summary>Шаблон подтверждения потребности с маркером <c>{need}</c>; не может быть <see langword="null"/>.</summary>
    public string HandoffConfirmationPrompt { get; init; } = string.Empty;

    /// <summary>Ответ на неоднозначное подтверждение; не может быть <see langword="null"/>.</summary>
    public string AmbiguousConfirmationResponse { get; init; } = string.Empty;
}
