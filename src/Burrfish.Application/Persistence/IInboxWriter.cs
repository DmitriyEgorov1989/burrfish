namespace Burrfish.Application.Persistence;

/// <summary>Надёжно сохраняет входящие события с дедупликацией.</summary>
public interface IInboxWriter
{
    /// <summary>Сохраняет событие и возвращает <see langword="true"/> только для новой записи.</summary>
    /// <param name="envelope">Входящее событие; не может быть <see langword="null"/>.</param>
    /// <param name="cancellationToken">Токен отмены операции.</param>
    /// <returns>Признак добавления новой записи.</returns>
    Task<bool> TryStoreAsync(InboxEnvelope envelope, CancellationToken cancellationToken);
}
