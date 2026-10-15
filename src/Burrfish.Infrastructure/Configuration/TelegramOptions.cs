namespace Burrfish.Infrastructure.Configuration;

/// <summary>Настройки Telegram-бота для уведомления менеджеров.</summary>
public sealed class TelegramOptions
{
    /// <summary>Имя секции конфигурации.</summary>
    public const string SectionName = "Telegram";

    /// <summary>Токен Telegram-бота; не может быть <see langword="null"/>.</summary>
    public string BotToken { get; init; } = string.Empty;

    /// <summary>Идентификатор закрытого чата; значение <c>0</c> означает отсутствие настройки.</summary>
    public long ChatId { get; init; }
}
