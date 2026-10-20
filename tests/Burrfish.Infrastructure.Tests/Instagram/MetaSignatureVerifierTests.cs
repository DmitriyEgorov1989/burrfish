using System.Security.Cryptography;
using System.Text;
using Burrfish.Infrastructure.Configuration;
using Burrfish.Infrastructure.Instagram;
using Microsoft.Extensions.Options;

namespace Burrfish.Infrastructure.Tests.Instagram;

public sealed class MetaSignatureVerifierTests
{
    private const string Secret = "test-app-secret";

    [Fact]
    public void Valid_signature_matches_original_utf8_bytes()
    {
        var body = "{\"text\":\"Привет\"}"u8.ToArray();
        var signature = Sign(body, Secret);
        var verifier = CreateVerifier(Secret);

        Assert.True(verifier.IsValid(body, signature));
        Assert.False(verifier.IsValid("{}"u8, signature));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("sha1=0011")]
    [InlineData("sha256=0011")]
    [InlineData("sha256=zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz")]
    public void Malformed_signature_is_rejected(string? signature)
    {
        Assert.False(CreateVerifier(Secret).IsValid("{}"u8, signature));
    }

    private static MetaSignatureVerifier CreateVerifier(string secret) => new(
        Options.Create(new InstagramOptions { AppSecret = secret }));

    private static string Sign(byte[] body, string secret) =>
        $"sha256={Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), body))}";
}
