namespace Burrfish.Application.Persistence;

/// <summary>Загружает и добавляет агрегаты диалогов.</summary>
public interface IConversationRepository
{
    /// <summary>Ищет отслеживаемый диалог по внешнему идентификатору.</summary>
    /// <param name="externalConversationId">Внешний идентификатор; не может быть <see langword="null"/>.</param>
    /// <param name="cancellationToken">Токен отмены операции.</param>
    /// <returns>Диалог либо <see langword="null"/>, если запись отсутствует.</returns>
    Task<StoredConversation?> FindAsync(
        string externalConversationId,
        CancellationToken cancellationToken);

    /// <summary>Добавляет новый диалог в текущую единицу сохранения.</summary>
    /// <param name="conversation">Новый диалог; не может быть <see langword="null"/>.</param>
    void Add(StoredConversation conversation);
}
