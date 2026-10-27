namespace Burrfish.Application.OpenAI;

/// <summary>Роль сообщения в ограниченной истории диалога.</summary>
public enum AiDialogMessageRole
{
    /// <summary>Сообщение клиента.</summary>
    User,

    /// <summary>Предыдущий ответ бота.</summary>
    Assistant
}

/// <summary>Элемент ограниченной истории диалога.</summary>
/// <param name="Role">Роль участника.</param>
/// <param name="Text">Текст сообщения; не может быть <see langword="null"/>.</param>
public sealed record AiDialogHistoryMessage(
    AiDialogMessageRole Role,
    string Text);

/// <summary>Данные для анализа одного клиентского сообщения.</summary>
/// <param name="Text">Текущий текст; не может быть <see langword="null"/>.</param>
/// <param name="History">
/// Предыдущие сообщения от старых к новым; не может быть <see langword="null"/>, допускается пустой список.
/// </param>
public sealed record AiDialogRequest(
    string Text,
    IReadOnlyList<AiDialogHistoryMessage> History);
