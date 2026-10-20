namespace Burrfish.Application.Instagram;

/// <summary>Проверяет подлинность исходного тела webhook Meta.</summary>
public interface IMetaSignatureVerifier
{
    /// <summary>Проверяет HMAC-подпись без изменения исходных байтов.</summary>
    /// <param name="body">Исходные байты HTTP-запроса.</param>
    /// <param name="signature">Значение заголовка подписи; <see langword="null"/> означает отсутствующую подпись.</param>
    /// <returns>Признак корректной подписи.</returns>
    bool IsValid(ReadOnlySpan<byte> body, string? signature);
}
