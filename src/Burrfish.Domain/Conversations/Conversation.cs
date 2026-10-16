namespace Burrfish.Domain.Conversations;

/// <summary>Корень агрегата диалога с клиентом.</summary>
public sealed class Conversation
{
    private Conversation()
    {
    }

    /// <summary>Текущее состояние диалога.</summary>
    public ConversationState State { get; private set; } = ConversationState.BotActive;

    /// <summary>
    /// Потребность, ожидающая подтверждения; <see langword="null"/> вне состояния ожидания.
    /// </summary>
    public string? PendingNeed { get; private set; }

    /// <summary>Показывает, разрешён ли автоматический ответ в текущем состоянии.</summary>
    public bool CanReply => State != ConversationState.OperatorHandoff;

    /// <summary>Начинает новый диалог в состоянии <see cref="ConversationState.BotActive"/>.</summary>
    /// <returns>Новый активный диалог.</returns>
    public static Conversation Start() => new();

    /// <summary>Сохраняет потребность и переводит диалог в ожидание подтверждения.</summary>
    /// <param name="need">Потребность клиента; не может быть <see langword="null"/> или пустой.</param>
    /// <returns>Результат перехода с кодом ожидаемой ошибки.</returns>
    public ConversationTransitionResult ProposeHandoff(string? need)
    {
        if (State != ConversationState.BotActive)
        {
            return new(false, "InvalidState");
        }

        if (string.IsNullOrWhiteSpace(need))
        {
            return new(false, "NeedRequired");
        }

        PendingNeed = need.Trim();
        State = ConversationState.AwaitingHandoffConfirmation;

        return new(true, null);
    }

    /// <summary>Фиксирует согласие клиента и запрещает дальнейшие ответы бота.</summary>
    /// <param name="confirmedAt">Момент получения согласия клиента.</param>
    /// <returns>Подтверждённый черновик либо ожидаемая ошибка без черновика.</returns>
    public HandoffConfirmationResult ConfirmHandoff(DateTimeOffset confirmedAt)
    {
        if (State != ConversationState.AwaitingHandoffConfirmation)
        {
            return new(false, "InvalidState", null);
        }

        if (string.IsNullOrWhiteSpace(PendingNeed))
        {
            return new(false, "NeedRequired", null);
        }

        var lead = new LeadDraft(PendingNeed, confirmedAt);

        PendingNeed = null;
        State = ConversationState.OperatorHandoff;

        return new(true, null, lead);
    }

    /// <summary>Фиксирует отказ клиента, очищает потребность и возобновляет диалог.</summary>
    /// <returns>Результат перехода с кодом ожидаемой ошибки.</returns>
    public ConversationTransitionResult DeclineHandoff()
    {
        if (State != ConversationState.AwaitingHandoffConfirmation)
        {
            return new(false, "InvalidState");
        }

        PendingNeed = null;
        State = ConversationState.BotActive;

        return new(true, null);
    }
}
