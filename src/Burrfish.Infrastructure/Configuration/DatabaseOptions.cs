namespace Burrfish.Infrastructure.Configuration;

/// <summary>Настройки подключения к PostgreSQL.</summary>
public sealed class DatabaseOptions
{
    /// <summary>Имя секции конфигурации.</summary>
    public const string SectionName = "Database";

    /// <summary>Строка подключения; не может быть <see langword="null"/> или пустой.</summary>
    public string ConnectionString { get; init; } = string.Empty;
}
