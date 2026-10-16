using Burrfish.Domain.Conversations;

namespace Burrfish.Application.Persistence;

/// <summary>Добавляет подтверждённые заявки клиентов.</summary>
public interface ILeadRepository
{
    /// <summary>Добавляет заявку в текущую единицу сохранения.</summary>
    /// <param name="conversation">Подтверждённый диалог; не может быть <see langword="null"/>.</param>
    /// <param name="draft">Черновик подтверждённой заявки; не может быть <see langword="null"/>.</param>
    /// <returns>Внутренний идентификатор новой заявки.</returns>
    Guid Add(StoredConversation conversation, LeadDraft draft);
}
