namespace Burrfish.Application.Instagram;

/// <summary>Результат одной попытки прикладной обработки сообщения.</summary>
/// <param name="Succeeded">Признак успешной обработки и возможности зафиксировать изменения.</param>
/// <param name="ErrorCode">Код ожидаемой причины повторной попытки; <see langword="null"/> при успехе.</param>
public sealed record ProcessIncomingMessageResult(bool Succeeded, string? ErrorCode);
