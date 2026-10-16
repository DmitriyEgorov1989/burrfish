namespace Burrfish.Application.Persistence;

/// <summary>Добавляет сохраняемые исходящие задания.</summary>
public interface IOutboxWriter
{
    /// <summary>Добавляет задание в текущую единицу сохранения.</summary>
    /// <param name="envelope">Исходящее задание; не может быть <see langword="null"/>.</param>
    void Add(OutboxEnvelope envelope);
}
