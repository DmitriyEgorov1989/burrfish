namespace Burrfish.Infrastructure.Configuration;

/// <summary>Настройки подключения одного Instagram Business-аккаунта.</summary>
public sealed class InstagramOptions
{
    /// <summary>Имя секции конфигурации.</summary>
    public const string SectionName = "Instagram";

    /// <summary>Идентификатор Instagram-аккаунта; не может быть <see langword="null"/>.</summary>
    public string AccountId { get; init; } = string.Empty;

    /// <summary>Секрет Meta-приложения; не может быть <see langword="null"/>.</summary>
    public string AppSecret { get; init; } = string.Empty;

    /// <summary>Токен проверки webhook; не может быть <see langword="null"/>.</summary>
    public string VerifyToken { get; init; } = string.Empty;

    /// <summary>Токен отправки сообщений; не может быть <see langword="null"/>.</summary>
    public string AccessToken { get; init; } = string.Empty;

    /// <summary>Версия Instagram Graph API; не может быть <see langword="null"/>.</summary>
    public string ApiVersion { get; init; } = string.Empty;

    /// <summary>Максимальная длительность одной отправки в секундах.</summary>
    public int SendTimeoutSeconds { get; init; } = 15;
}
