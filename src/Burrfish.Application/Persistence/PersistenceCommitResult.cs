namespace Burrfish.Application.Persistence;

/// <summary>Результат атомарного сохранения изменений диалога.</summary>
/// <param name="Succeeded">Признак успешного сохранения.</param>
/// <param name="ErrorCode">Код ожидаемого конфликта; <see langword="null"/> при успехе.</param>
public sealed record PersistenceCommitResult(bool Succeeded, string? ErrorCode);
