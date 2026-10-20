namespace Burrfish.Application.Persistence;

/// <summary>Входящее событие внешней платформы до бизнес-обработки.</summary>
/// <param name="ExternalEventId">Уникальный идентификатор события; не может быть <see langword="null"/>.</param>
/// <param name="Payload">JSON одного внешнего события с контекстом webhook; не может быть <see langword="null"/>.</param>
/// <param name="ReceivedAt">Момент получения события в UTC.</param>
public sealed record InboxEnvelope(
    string ExternalEventId,
    string Payload,
    DateTimeOffset ReceivedAt);
