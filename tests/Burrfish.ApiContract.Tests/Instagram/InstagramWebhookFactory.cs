using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Burrfish.Application.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Burrfish.ApiContract.Tests.Instagram;

internal sealed class InstagramWebhookFactory(
    Action<IServiceCollection>? configureServices = null)
    : WebApplicationFactory<Program>
{
    public const string AccountId = "100000000000001";
    public const string AppSecret = "test-app-secret";
    public const string VerifyToken = "test-verify-token";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:ConnectionString"] = "Host=localhost;Database=test;Username=test;Password=test",
                ["Instagram:AccountId"] = AccountId,
                ["Instagram:AppSecret"] = AppSecret,
                ["Instagram:VerifyToken"] = VerifyToken,
                ["Instagram:AccessToken"] = "test-access-token",
                ["Instagram:ApiVersion"] = "v26.0",
                ["OpenAI:ApiKey"] = "test-openai-key",
                ["OpenAI:Model"] = "test-model",
                ["InboxProcessing:Enabled"] = "false",
                ["OutboxProcessing:Enabled"] = "false",
                ["DataRetention:Enabled"] = "false",
                ["Observability:Enabled"] = "false",
                ["Telegram:BotToken"] = "test-bot-token",
                ["Telegram:ChatId"] = "1"
            });
        });
        builder.ConfigureTestServices(services => configureServices?.Invoke(services));
    }

    public HttpRequestMessage CreateSignedPost(byte[] body)
    {
        var signature = HMACSHA256.HashData(Encoding.UTF8.GetBytes(AppSecret), body);
        var request = new HttpRequestMessage(HttpMethod.Post, "/webhooks/instagram")
        {
            Content = new ByteArrayContent(body)
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        request.Headers.TryAddWithoutValidation(
            "X-Hub-Signature-256",
            $"sha256={Convert.ToHexString(signature)}");
        return request;
    }

    public static void ReplaceInboxWriter(IServiceCollection services, IInboxWriter writer)
    {
        services.RemoveAll<IInboxWriter>();
        services.AddSingleton(writer);
    }
}
