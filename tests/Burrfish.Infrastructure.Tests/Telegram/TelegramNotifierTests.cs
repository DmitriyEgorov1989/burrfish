using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Burrfish.Application.Processing;
using Burrfish.Application.Telegram;
using Burrfish.Infrastructure.Configuration;
using Burrfish.Infrastructure.Telegram;
using Microsoft.Extensions.Options;

namespace Burrfish.Infrastructure.Tests.Telegram;

public sealed class TelegramNotifierTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 10, 27, 13, 14, 15, TimeSpan.Zero);

    [Fact]
    public async Task Successful_send_uses_official_send_message_contract()
    {
        HttpRequestMessage? captured = null;
        string? body = null;
        var handler = new StubHttpHandler(async (request, cancellationToken) =>
        {
            captured = request;
            body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    ok = true,
                    result = new { message_id = 321 }
                })
            };
        });

        var result = await CreateNotifier(handler).SendLeadAsync(Notification(), CancellationToken.None);

        Assert.Equal(DeliveryStatus.Sent, result.Status);
        Assert.Equal("321", result.ExternalId);
        Assert.Equal(HttpMethod.Post, captured!.Method);
        Assert.Equal(
            "https://api.telegram.org/botsynthetic-bot-token/sendMessage",
            captured.RequestUri!.AbsoluteUri);
        using var document = JsonDocument.Parse(body!);
        Assert.Equal(-1001234567890, document.RootElement.GetProperty("chat_id").GetInt64());
        Assert.Equal("HTML", document.RootElement.GetProperty("parse_mode").GetString());
        Assert.True(document.RootElement.GetProperty("link_preview_options").GetProperty("is_disabled").GetBoolean());
        Assert.Contains("Новая заявка", document.RootElement.GetProperty("text").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Flood_control_uses_retry_after_seconds()
    {
        var result = await CreateNotifier(new StubHttpHandler((_, _) => Task.FromResult(
            TelegramError(HttpStatusCode.TooManyRequests, 429, "Too Many Requests", retryAfter: 75))))
            .SendLeadAsync(Notification(), CancellationToken.None);

        Assert.Equal(DeliveryStatus.Retry, result.Status);
        Assert.Equal("429", result.ErrorCode);
        Assert.Equal("Too Many Requests", result.ErrorMessage);
        Assert.Equal(TimeSpan.FromSeconds(75), result.RetryAfter);
    }

    [Theory]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    public async Task Temporary_http_status_requests_retry(HttpStatusCode statusCode)
    {
        var result = await CreateNotifier(new StubHttpHandler((_, _) => Task.FromResult(
            TelegramError(statusCode, (int)statusCode, "Temporary Telegram failure."))))
            .SendLeadAsync(Notification(), CancellationToken.None);

        Assert.Equal(DeliveryStatus.Retry, result.Status);
        Assert.Equal(((int)statusCode).ToString(), result.ErrorCode);
        Assert.Equal("Temporary Telegram failure.", result.ErrorMessage);
    }

    [Fact]
    public async Task Permanent_error_preserves_code_and_description()
    {
        var result = await CreateNotifier(new StubHttpHandler((_, _) => Task.FromResult(
            TelegramError(HttpStatusCode.Forbidden, 403, "Forbidden: bot was blocked."))))
            .SendLeadAsync(Notification(), CancellationToken.None);

        Assert.Equal(DeliveryStatus.Failed, result.Status);
        Assert.Equal("403", result.ErrorCode);
        Assert.Equal("Forbidden: bot was blocked.", result.ErrorMessage);
    }

    [Fact]
    public async Task Network_error_redacts_bot_token_and_does_not_retry_internally()
    {
        var calls = 0;
        var handler = new StubHttpHandler((_, _) =>
        {
            calls++;
            throw new HttpRequestException(
                "Failure at https://api.telegram.org/botsynthetic-bot-token/sendMessage");
        });

        var result = await CreateNotifier(handler).SendLeadAsync(Notification(), CancellationToken.None);

        Assert.Equal(DeliveryStatus.Retry, result.Status);
        Assert.Equal(nameof(HttpRequestException), result.ErrorCode);
        Assert.DoesNotContain("synthetic-bot-token", result.ErrorMessage, StringComparison.Ordinal);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Caller_cancellation_is_propagated()
    {
        var handler = new StubHttpHandler((_, cancellationToken) =>
            Task.FromCanceled<HttpResponseMessage>(cancellationToken));
        using var source = new CancellationTokenSource();
        source.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CreateNotifier(handler).SendLeadAsync(Notification(), source.Token));
    }

    [Fact]
    public async Task Oversized_response_is_rejected()
    {
        var handler = new StubHttpHandler((_, _) => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StreamContent(new MemoryStream(new byte[65_537]))
            }));

        var result = await CreateNotifier(handler).SendLeadAsync(Notification(), CancellationToken.None);

        Assert.Equal(DeliveryStatus.Failed, result.Status);
        Assert.Equal("ResponseTooLarge", result.ErrorCode);
    }

    [Fact]
    public async Task Valid_json_of_unexpected_shape_returns_controlled_retry()
    {
        var handler = new StubHttpHandler((_, _) => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.BadGateway)
            {
                Content = JsonContent.Create(Array.Empty<object>())
            }));

        var result = await CreateNotifier(handler).SendLeadAsync(Notification(), CancellationToken.None);

        Assert.Equal(DeliveryStatus.Retry, result.Status);
        Assert.Equal("HTTP502", result.ErrorCode);
    }

    private static TelegramNotifier CreateNotifier(HttpMessageHandler handler) => new(
        new HttpClient(handler) { BaseAddress = new Uri("https://api.telegram.org/") },
        Options.Create(new TelegramOptions
        {
            BotToken = "synthetic-bot-token",
            ChatId = -1001234567890,
            TimeoutSeconds = 15
        }),
        new TelegramLeadMessageFormatter());

    private static LeadNotification Notification() => new(
        Guid.Parse("11111111-2222-3333-4444-555555555555"),
        "instagram-123",
        null,
        "Нужна консультация",
        Now);

    private static HttpResponseMessage TelegramError(
        HttpStatusCode statusCode,
        int errorCode,
        string description,
        int? retryAfter = null) => new(statusCode)
    {
        Content = JsonContent.Create(new
        {
            ok = false,
            error_code = errorCode,
            description,
            parameters = retryAfter is null ? null : new { retry_after = retryAfter }
        })
    };

    private sealed class StubHttpHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handle)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => handle(request, cancellationToken);
    }
}
