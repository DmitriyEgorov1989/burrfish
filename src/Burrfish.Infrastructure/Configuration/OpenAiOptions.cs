namespace Burrfish.Infrastructure.Configuration;

/// <summary>Настройки обращения к OpenAI.</summary>
public sealed class OpenAiOptions
{
    /// <summary>Имя секции конфигурации.</summary>
    public const string SectionName = "OpenAI";

    /// <summary>Ключ OpenAI API; не может быть <see langword="null"/>.</summary>
    public string ApiKey { get; init; } = string.Empty;

    /// <summary>Идентификатор используемой модели; не может быть <see langword="null"/>.</summary>
    public string Model { get; init; } = string.Empty;

    /// <summary>Максимальная длительность одного обращения в секундах.</summary>
    public int TimeoutSeconds { get; init; } = 15;
}
