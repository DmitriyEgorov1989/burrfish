namespace Burrfish.Domain.Conversations;

/// <summary>Результат подтверждения передачи диалога менеджеру.</summary>
/// <param name="Succeeded">Признак выполненной передачи.</param>
/// <param name="ErrorCode">Код ожидаемой ошибки; <see langword="null"/> при успехе.</param>
/// <param name="Lead">Черновик заявки; <see langword="null"/>, если переход отклонён.</param>
public sealed record HandoffConfirmationResult(
    bool Succeeded,
    string? ErrorCode,
    LeadDraft? Lead);
