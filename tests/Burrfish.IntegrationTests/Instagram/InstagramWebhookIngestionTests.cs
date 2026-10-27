using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Burrfish.Application.Persistence;
using Burrfish.Infrastructure.Persistence;
using Burrfish.IntegrationTests.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Burrfish.IntegrationTests.Instagram;

[Collection(PostgresCollection.Name)]
public sealed class InstagramWebhookIngestionTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Repeated_batch_is_acknowledged_and_stored_once_per_message()
    {
        using var factory = new WebhookFactory(fixture.ConnectionString);
        using var client = factory.CreateClient();
        var firstId = Guid.NewGuid().ToString("N");
        var secondId = Guid.NewGuid().ToString("N");
        var body = CreateBatch(firstId, secondId);

        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var request = factory.CreateSignedPost(body);
            using var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        await using var db = fixture.CreateContext();
        var stored = await db.InboxEvents
            .Where(entity => entity.ExternalEventId == firstId
                || entity.ExternalEventId == secondId)
            .OrderBy(entity => entity.ExternalEventId)
            .ToArrayAsync();

        Assert.Equal(2, stored.Length);
        Assert.All(stored, entity => Assert.Equal(TimeSpan.Zero, entity.ReceivedAt.Offset));
    }

    [Fact]
    public async Task Retry_after_partial_batch_stores_only_missing_message()
    {
        var firstId = Guid.NewGuid().ToString("N");
        var secondId = Guid.NewGuid().ToString("N");
        await using (var seedDb = fixture.CreateContext())
        {
            var writer = new InboxWriter(seedDb);
            await writer.TryStoreAsync(
                new InboxEnvelope(firstId, "{}", DateTimeOffset.UtcNow),
                CancellationToken.None);
        }

        using var factory = new WebhookFactory(fixture.ConnectionString);
        using var client = factory.CreateClient();
        using var request = factory.CreateSignedPost(CreateBatch(firstId, secondId));
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var verificationDb = fixture.CreateContext();
        var count = await verificationDb.InboxEvents.CountAsync(
            entity => entity.ExternalEventId == firstId
                || entity.ExternalEventId == secondId);
        Assert.Equal(2, count);
    }

    private static byte[] CreateBatch(params string[] messageIds) =>
        JsonSerializer.SerializeToUtf8Bytes(new
        {
            @object = "instagram",
            entry = new[]
            {
                new
                {
                    id = WebhookFactory.AccountId,
                    time = 1792335600000,
                    messaging = messageIds.Select(messageId => new
                    {
                        sender = new { id = "200000000000001" },
                        recipient = new { id = WebhookFactory.AccountId },
                        timestamp = 1792335600000,
                        message = new { mid = messageId, text = "Тестовый запрос" }
                    })
                }
            }
        });

    private sealed class WebhookFactory(string connectionString)
        : WebApplicationFactory<Program>
    {
        public const string AccountId = "100000000000001";
        private const string AppSecret = "integration-test-app-secret";

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Database:ConnectionString"] = connectionString,
                    ["Instagram:AccountId"] = AccountId,
                    ["Instagram:AppSecret"] = AppSecret,
                    ["Instagram:VerifyToken"] = "integration-verify-token",
                    ["Instagram:AccessToken"] = "integration-access-token",
                    ["Instagram:ApiVersion"] = "v26.0",
                    ["OpenAI:ApiKey"] = "integration-openai-key",
                    ["OpenAI:Model"] = "integration-model",
                    ["InboxProcessing:Enabled"] = "false",
                    ["OutboxProcessing:Enabled"] = "false",
                    ["Telegram:BotToken"] = "integration-bot-token",
                    ["Telegram:ChatId"] = "1"
                }));
        }

        public HttpRequestMessage CreateSignedPost(byte[] body)
        {
            var digest = HMACSHA256.HashData(Encoding.UTF8.GetBytes(AppSecret), body);
            var request = new HttpRequestMessage(HttpMethod.Post, "/webhooks/instagram")
            {
                Content = new ByteArrayContent(body)
            };
            request.Headers.TryAddWithoutValidation(
                "X-Hub-Signature-256",
                $"sha256={Convert.ToHexString(digest)}");
            return request;
        }
    }
}
