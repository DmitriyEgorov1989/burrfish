namespace Burrfish.Application.Persistence;

/// <summary>Атомарно сохраняет изменения текущего сценария диалога.</summary>
public interface IConversationPersistence
{
    /// <summary>
    /// Сохраняет подготовленные изменения диалога, заявки и исходящих заданий.
    /// После конфликта текущую единицу сохранения нельзя использовать повторно.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены операции.</param>
    /// <returns>Успех либо ожидаемый конфликт сохранения.</returns>
    Task<PersistenceCommitResult> CommitAsync(CancellationToken cancellationToken);
}
