namespace Burrfish.Domain.Conversations;

/// <summary>Состояние автоматической обработки диалога.</summary>
public enum ConversationState
{
    /// <summary>Бот отвечает на вопросы и уточняет потребность.</summary>
    BotActive,

    /// <summary>Бот ожидает согласия клиента на передачу менеджеру.</summary>
    AwaitingHandoffConfirmation,

    /// <summary>Диалог передан менеджеру; автоматические ответы запрещены.</summary>
    OperatorHandoff
}
