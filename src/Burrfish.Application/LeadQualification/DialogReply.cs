using Burrfish.Domain.Conversations;

namespace Burrfish.Application.LeadQualification;

/// <summary>Действия, выбранные при обработке клиентского сообщения.</summary>
/// <param name="Text">Ответ; <see langword="null"/>, если бот должен молчать.</param>
/// <param name="Lead">Подтверждённая заявка; <see langword="null"/>, если она не создана.</param>
public sealed record DialogReply(string? Text, LeadDraft? Lead);
