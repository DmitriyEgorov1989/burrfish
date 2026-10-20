using System.Text.Json;
using Burrfish.Infrastructure.Configuration;
using Burrfish.Infrastructure.Instagram;
using Microsoft.Extensions.Options;

namespace Burrfish.Infrastructure.Tests.Instagram;

public sealed class InstagramEventNormalizerTests
{
    private const string AccountId = "100000000000001";

    [Fact]
    public void Text_fixture_is_normalized_without_username()
    {
        var payload = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "Instagram",
            "Fixtures",
            "text-message.json"));

        var message = CreateNormalizer().Normalize(payload);

        Assert.NotNull(message);
        Assert.Equal("mid.synthetic.1", message.ExternalEventId);
        Assert.Equal("100000000000001:200000000000001", message.ConversationId);
        Assert.Equal("200000000000001", message.SenderId);
        Assert.Null(message.Username);
        Assert.Equal("Тестовый запрос", message.Text);
        Assert.Equal(
            new DateTimeOffset(2026, 10, 18, 15, 0, 0, TimeSpan.Zero),
            message.ReceivedAt);
    }

    [Theory]
    [InlineData("{")]
    [InlineData("unknown")]
    [InlineData("attachment")]
    [InlineData("echo")]
    public void Unsupported_event_is_ignored(string scenario)
    {
        var payload = scenario == "{"
            ? scenario
            : CreatePayload(scenario);

        Assert.Null(CreateNormalizer().Normalize(payload));
    }

    private static InstagramEventNormalizer CreateNormalizer() => new(
        Options.Create(new InstagramOptions { AccountId = AccountId }));

    private static string CreatePayload(string scenario)
    {
        object messaging = scenario switch
        {
            "unknown" => new { reaction = new { emoji = "like" } },
            "attachment" => new
            {
                sender = new { id = "sender-1" },
                recipient = new { id = AccountId },
                timestamp = 1792335600000,
                message = new { mid = "mid-1", attachments = Array.Empty<object>() }
            },
            "echo" => new
            {
                sender = new { id = "sender-1" },
                recipient = new { id = AccountId },
                timestamp = 1792335600000,
                message = new { mid = "mid-1", text = "echo", is_echo = true }
            },
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };

        return JsonSerializer.Serialize(new
        {
            @object = "instagram",
            entry = new[] { new { id = AccountId, messaging = new[] { messaging } } }
        });
    }
}
