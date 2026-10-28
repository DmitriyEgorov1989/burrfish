using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Burrfish.Application.Instagram;
using Burrfish.Application.OpenAI;
using Burrfish.Application.Telegram;
using Burrfish.Domain.Conversations;
using Burrfish.Infrastructure.Configuration;
using Burrfish.Infrastructure.Instagram;
using Burrfish.Infrastructure.OpenAI;
using Burrfish.Infrastructure.Persistence;
using Burrfish.Infrastructure.Persistence.Entities;
using Burrfish.Infrastructure.Processing;
using Burrfish.Infrastructure.Telegram;
using Burrfish.IntegrationTests.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Burrfish.IntegrationTests.EndToEnd;

[Collection(PostgresCollection.Name)]
public sealed class LeadFlowTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Signed_webhooks_complete_lead_flow_once()
    {
        await ClearDatabaseAsync();
        using var factory = new LeadFlowFactory(fixture.ConnectionString);
        using var client = factory.CreateClient();
        var senderId = Guid.NewGuid().ToString("N");

        var question = CreateWebhook(Guid.NewGuid().ToString("N"), senderId, "Сколько стоит?");
        await SendTwiceAsync(client, factory, question);
        Assert.True(await ProcessInboxAsync(factory));
        Assert.True(await DispatchOutboxAsync(factory));

        var confirmation = CreateWebhook(Guid.NewGuid().ToString("N"), senderId, "Да");
        await SendTwiceAsync(client, factory, confirmation);
        Assert.True(await ProcessInboxAsync(factory));
        Assert.True(await DispatchOutboxAsync(factory));

        var followUp = CreateWebhook(Guid.NewGuid().ToString("N"), senderId, "Ещё вопрос");
        await SendTwiceAsync(client, factory, followUp);
        Assert.True(await ProcessInboxAsync(factory));
        Assert.False(await DispatchOutboxAsync(factory));

        Assert.Equal(1, factory.OpenAi.Calls);
        Assert.Equal(1, factory.Instagram.Calls);
        Assert.Equal(1, factory.Telegram.Calls);
        using (var reply = JsonDocument.Parse(factory.Instagram.RequestBodies.Single()))
        {
            var text = reply.RootElement.GetProperty("message").GetProperty("text").GetString();
            Assert.Contains("Стоимость зависит от вашей задачи.", text);
            Assert.Contains("Передать ваш вопрос менеджеру?", text);
        }
        using (var notification = JsonDocument.Parse(factory.Telegram.RequestBodies.Single()))
        {
            Assert.Equal(1, notification.RootElement.GetProperty("chat_id").GetInt64());
            var text = notification.RootElement.GetProperty("text").GetString();
            Assert.Contains("Сколько стоит?", text);
            Assert.Contains(senderId, text);
        }

        await using var db = fixture.CreateContext();
        Assert.Equal(3, await db.InboxEvents.CountAsync());
        Assert.Equal(3, await db.InboxEvents.CountAsync(x => x.Status == InboxEventStatus.Processed));
        Assert.Equal(2, await db.OutboxMessages.CountAsync(x => x.Status == OutboxMessageStatus.Sent));
        Assert.Equal(ConversationState.OperatorHandoff,
            (await db.Conversations.SingleAsync(x => x.SenderId == senderId)).Model.State);
        var lead = await db.Leads.SingleAsync(x => x.SenderId == senderId);
        Assert.Equal("Сколько стоит?", lead.Need);
        Assert.Equal(LeadNotificationStatus.Sent, lead.NotificationStatus);
    }

    [Fact]
    public async Task Invalid_signature_stops_before_inbox_and_external_http()
    {
        await ClearDatabaseAsync();
        using var factory = new LeadFlowFactory(fixture.ConnectionString);
        using var client = factory.CreateClient();
        var body = CreateWebhook(Guid.NewGuid().ToString("N"), Guid.NewGuid().ToString("N"), "Сколько стоит?");
        using var request = factory.CreatePost(body, "sha256=" + new string('0', 64));

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, factory.OpenAi.Calls);
        Assert.Equal(0, factory.Instagram.Calls);
        Assert.Equal(0, factory.Telegram.Calls);
        await using var db = fixture.CreateContext();
        Assert.Empty(await db.InboxEvents.ToListAsync());
    }

    private async Task ClearDatabaseAsync()
    {
        await using var db = fixture.CreateContext();
        await db.OutboxMessages.ExecuteDeleteAsync();
        await db.Leads.ExecuteDeleteAsync();
        await db.InboxEvents.ExecuteDeleteAsync();
        await db.Conversations.ExecuteDeleteAsync();
    }

    private static async Task SendTwiceAsync(HttpClient client, LeadFlowFactory factory, byte[] body)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var request = factory.CreateSignedPost(body);
            using var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    private static async Task<bool> ProcessInboxAsync(LeadFlowFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider
            .GetRequiredService<InboxProcessor>()
            .ProcessNextAsync(CancellationToken.None);
    }

    private static async Task<bool> DispatchOutboxAsync(LeadFlowFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider
            .GetRequiredService<OutboxDispatcher>()
            .DispatchNextAsync(CancellationToken.None);
    }

    private static byte[] CreateWebhook(string messageId, string senderId, string text) =>
        JsonSerializer.SerializeToUtf8Bytes(new
        {
            @object = "instagram",
            entry = new[]
            {
                new
                {
                    id = LeadFlowFactory.AccountId,
                    time = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    messaging = new[]
                    {
                        new
                        {
                            sender = new { id = senderId },
                            recipient = new { id = LeadFlowFactory.AccountId },
                            timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                            message = new { mid = messageId, text }
                        }
                    }
                }
            }
        });

    private sealed class LeadFlowFactory(string connectionString) : WebApplicationFactory<Program>
    {
        public const string AccountId = "100000000000001";
        private const string AppSecret = "integration-test-app-secret";

        public RecordingHandler OpenAi { get; } = new(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new
            {
                status = "completed",
                output = new[]
                {
                    new
                    {
                        type = "message",
                        role = "assistant",
                        content = new[]
                        {
                            new { type = "output_text", text = "{\"kind\":\"price\",\"text\":null}" }
                        }
                    }
                }
            })
        });

        public RecordingHandler Instagram { get; } = new(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { message_id = "synthetic-instagram-message" })
        });

        public RecordingHandler Telegram { get; } = new(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { ok = true, result = new { message_id = 1 } })
        });

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
                    ["Telegram:BotToken"] = "integration-bot-token",
                    ["Telegram:ChatId"] = "1",
                    ["InboxProcessing:Enabled"] = "false",
                    ["OutboxProcessing:Enabled"] = "false",
                    ["DataRetention:Enabled"] = "false",
                    ["Observability:Enabled"] = "false"
                }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IAiDialogClient>();
                services.RemoveAll<IInstagramMessageSender>();
                services.RemoveAll<ITelegramNotifier>();
                services.AddSingleton<IAiDialogClient>(provider => new OpenAiDialogClient(
                    CreateHttpClient(OpenAi, "https://api.openai.com/v1/"),
                    provider.GetRequiredService<IOptions<OpenAiOptions>>(),
                    provider.GetRequiredService<OpenAiRequestBuilder>(),
                    provider.GetRequiredService<OpenAiResponseReader>()));
                services.AddSingleton<IInstagramMessageSender>(provider => new InstagramGraphApiClient(
                    CreateHttpClient(Instagram, "https://graph.instagram.com/"),
                    provider.GetRequiredService<IOptions<InstagramOptions>>(),
                    provider.GetRequiredService<TimeProvider>()));
                services.AddSingleton<ITelegramNotifier>(provider => new TelegramNotifier(
                    CreateHttpClient(Telegram, "https://api.telegram.org/"),
                    provider.GetRequiredService<IOptions<TelegramOptions>>(),
                    provider.GetRequiredService<TelegramLeadMessageFormatter>()));
            });
        }

        public HttpRequestMessage CreateSignedPost(byte[] body)
        {
            var digest = HMACSHA256.HashData(Encoding.UTF8.GetBytes(AppSecret), body);
            return CreatePost(body, $"sha256={Convert.ToHexString(digest)}");
        }

        public HttpRequestMessage CreatePost(byte[] body, string signature)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "/webhooks/instagram")
            {
                Content = new ByteArrayContent(body)
            };
            request.Headers.TryAddWithoutValidation("X-Hub-Signature-256", signature);
            return request;
        }

        private static HttpClient CreateHttpClient(HttpMessageHandler handler, string baseAddress) => new(handler)
        {
            BaseAddress = new Uri(baseAddress),
            Timeout = Timeout.InfiniteTimeSpan
        };
    }

    public sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        private int calls;

        public int Calls => Volatile.Read(ref calls);

        public List<string> RequestBodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref calls);
            RequestBodies.Add(request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken));
            return respond(request);
        }
    }
}
