namespace Burrfish.Application.OpenAI;

/// <summary>Получает проверенное решение для продолжения диалога.</summary>
public interface IAiDialogClient
{
    /// <summary>Анализирует сообщение и ограниченную историю.</summary>
    /// <param name="request">Данные диалога; не могут быть <see langword="null"/>.</param>
    /// <param name="cancellationToken">Токен отмены вызывающего кода.</param>
    /// <returns>Решение или ожидаемая ошибка интеграции.</returns>
    Task<AiDialogResult> DecideAsync(
        AiDialogRequest request,
        CancellationToken cancellationToken);
}
