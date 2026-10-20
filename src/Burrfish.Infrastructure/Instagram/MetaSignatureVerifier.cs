using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using Burrfish.Application.Instagram;
using Burrfish.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace Burrfish.Infrastructure.Instagram;

/// <summary>Проверяет подпись Meta по исходным байтам HTTP-запроса.</summary>
public sealed class MetaSignatureVerifier(IOptions<InstagramOptions> options)
    : IMetaSignatureVerifier
{
    private const string Prefix = "sha256=";
    private const int HashLength = 32;
    private const int HexLength = HashLength * 2;

    /// <inheritdoc />
    public bool IsValid(ReadOnlySpan<byte> body, string? signature)
    {
        if (signature is null
            || signature.Length != Prefix.Length + HexLength
            || !signature.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        Span<byte> suppliedHash = stackalloc byte[HashLength];
        var status = Convert.FromHexString(
            signature.AsSpan(Prefix.Length),
            suppliedHash,
            out var consumed,
            out var written);

        if (status != OperationStatus.Done
            || consumed != HexLength
            || written != HashLength)
        {
            return false;
        }

        var key = Encoding.UTF8.GetBytes(options.Value.AppSecret);
        try
        {
            Span<byte> expectedHash = stackalloc byte[HashLength];
            HMACSHA256.HashData(key, body, expectedHash);
            return CryptographicOperations.FixedTimeEquals(expectedHash, suppliedHash);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }
}
