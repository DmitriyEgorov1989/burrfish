namespace Burrfish.Application.Instagram;

/// <summary>Подготовленный текстовый ответ клиенту Instagram.</summary>
/// <param name="RecipientId">Идентификатор получателя; не может быть <see langword="null"/>.</param>
/// <param name="Text">Текст ответа; не может быть <see langword="null"/>.</param>
public sealed record InstagramReply(string RecipientId, string Text);
