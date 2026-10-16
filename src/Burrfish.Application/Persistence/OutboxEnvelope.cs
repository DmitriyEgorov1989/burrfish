namespace Burrfish.Application.Persistence;

/// <summary>Исходящее задание до фиксации в базе.</summary>
/// <param name="Id">Внутренний идентификатор задания.</param>
/// <param name="DeduplicationKey">Уникальный ключ логического задания; не может быть <see langword="null"/>.</param>
/// <param name="Type">Версионированный тип задания; не может быть <see langword="null"/>.</param>
/// <param name="Payload">JSON задания; не может быть <see langword="null"/>.</param>
/// <param name="CreatedAt">Момент создания задания в UTC.</param>
public sealed record OutboxEnvelope(
    Guid Id,
    string DeduplicationKey,
    string Type,
    string Payload,
    DateTimeOffset CreatedAt);
