using System.Text.Json;
using Burrfish.Infrastructure.Configuration;
using Burrfish.Infrastructure.Instagram;
using Microsoft.Extensions.Options;

namespace Burrfish.Infrastructure.Tests.Instagram;

public sealed class MetaWebhookParserTests
{
    private const string AccountId = "account-1";
    private static readonly DateTimeOffset ReceivedAt =
        new(2026, 10, 18, 15, 0, 0, TimeSpan.FromHours(3));

    [Fact]
    public void Batch_is_split_into_one_payload_per_message()
    {
        var parser = CreateParser();
        var body = CreatePayload(AccountId, "mid-1", "mid-2");

        var envelopes = parser.Parse(body, ReceivedAt);

        Assert.NotNull(envelopes);
        Assert.Equal(["mid-1", "mid-2"], envelopes.Select(item => item.ExternalEventId));
        Assert.All(envelopes, envelope => Assert.Equal(TimeSpan.Zero, envelope.ReceivedAt.Offset));
        Assert.All(envelopes, envelope =>
        {
            using var document = JsonDocument.Parse(envelope.Payload);
            Assert.Single(document.RootElement.GetProperty("entry")[0].GetProperty("messaging").EnumerateArray());
        });
    }

    [Fact]
    public void Invalid_later_message_rejects_entire_batch()
    {
        var body = System.Text.Encoding.UTF8.GetBytes($$"""
            {
              "object": "instagram",
              "entry": [{
                "id": "{{AccountId}}",
                "messaging": [
                  { "message": { "mid": "mid-1", "text": "one" } },
                  { "message": { "text": "missing id" } }
                ]
              }]
            }
            """);

        Assert.Null(CreateParser().Parse(body, ReceivedAt));
    }

    [Fact]
    public void Foreign_account_and_non_message_events_are_ignored()
    {
        var parser = CreateParser();
        var foreign = parser.Parse(CreatePayload("account-2", "mid-1"), ReceivedAt);
        var unsupported = parser.Parse(
            System.Text.Encoding.UTF8.GetBytes($$"""
              { "object": "instagram", "entry": [{ "id": "{{AccountId}}", "messaging": [{ "reaction": {} }] }] }
              """),
            ReceivedAt);

        Assert.NotNull(foreign);
        Assert.Empty(foreign);
        Assert.NotNull(unsupported);
        Assert.Empty(unsupported);
    }

    [Theory]
    [InlineData("{")]
    [InlineData("[]")]
    [InlineData("{\"object\":\"instagram\"}")]
    public void Malformed_structure_is_rejected(string json)
    {
        Assert.Null(CreateParser().Parse(System.Text.Encoding.UTF8.GetBytes(json), ReceivedAt));
    }

    private static MetaWebhookParser CreateParser() => new(
        Options.Create(new InstagramOptions { AccountId = AccountId }));

    private static byte[] CreatePayload(string accountId, params string[] messageIds) =>
        JsonSerializer.SerializeToUtf8Bytes(new
        {
            @object = "instagram",
            entry = new[]
            {
                new
                {
                    id = accountId,
                    time = 1792335600000,
                    messaging = messageIds.Select(id => new
                    {
                        sender = new { id = "sender-1" },
                        recipient = new { id = accountId },
                        timestamp = 1792335600000,
                        message = new { mid = id, text = "Тест" }
                    })
                }
            }
        });
}
