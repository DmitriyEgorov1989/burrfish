namespace Burrfish.Domain.Conversations;

/// <summary>Результат перехода, не создающего черновик заявки.</summary>
/// <param name="Succeeded">Признак выполненного перехода.</param>
/// <param name="ErrorCode">Код ожидаемой ошибки; <see langword="null"/> при успехе.</param>
public sealed record ConversationTransitionResult(bool Succeeded, string? ErrorCode);
