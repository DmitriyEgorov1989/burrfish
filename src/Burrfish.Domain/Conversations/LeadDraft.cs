namespace Burrfish.Domain.Conversations;

/// <summary>Подтверждённая клиентом потребность для последующего сохранения заявки.</summary>
public sealed class LeadDraft
{
    internal LeadDraft(string need, DateTimeOffset createdAt)
    {
        Need = need;
        CreatedAt = createdAt.ToUniversalTime();
    }

    /// <summary>Подтверждённая непустая потребность; не может быть <see langword="null"/>.</summary>
    public string Need { get; }

    /// <summary>Момент подтверждения передачи в UTC.</summary>
    public DateTimeOffset CreatedAt { get; }
}
