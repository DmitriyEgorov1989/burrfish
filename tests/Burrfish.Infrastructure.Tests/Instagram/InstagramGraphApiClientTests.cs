using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Burrfish.Application.Instagram;
using Burrfish.Application.Processing;
using Burrfish.Infrastructure.Configuration;
using Burrfish.Infrastructure.Instagram;
using Microsoft.Extensions.Options;

namespace Burrfish.Infrastructure.Tests.Instagram;

public sealed class InstagramGraphApiClientTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 13, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Successful_send_uses_instagram_login_contract()
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
                    recipient_id = "recipient-1",
                    message_id = "message-1"
                })
            };
        });

        var result = await CreateClient(handler).SendAsync(
            new InstagramReply("recipient-1", "Ответ \"клиенту\".\nВторая строка."),
            CancellationToken.None);

        Assert.Equal(DeliveryStatus.Sent, result.Status);
        Assert.Equal("message-1", result.ExternalId);
        Assert.Equal(HttpMethod.Post, captured!.Method);
        Assert.Equal(
            "https://graph.instagram.com/v26.0/account-1/messages",
            captured.RequestUri!.AbsoluteUri);
        Assert.Equal("Bearer", captured.Headers.Authorization!.Scheme);
        Assert.Equal("access-token", captured.Headers.Authorization.Parameter);
        using var document = JsonDocument.Parse(body!);
        Assert.Equal("recipient-1", document.RootElement.GetProperty("recipient").GetProperty("id").GetString());
        Assert.Equal(
            "Ответ \"клиенту\".\nВторая строка.",
            document.RootElement.GetProperty("message").GetProperty("text").GetString());
    }

    [Theory]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    public async Task Temporary_http_errors_request_retry(HttpStatusCode statusCode)
    {
        var response = MetaError(statusCode, 2, "Temporary failure.");
        response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(
            TimeSpan.FromSeconds(45));

        var result = await CreateClient(new StubHttpHandler((_, _) => Task.FromResult(response)))
            .SendAsync(new InstagramReply("recipient-1", "Текст"), CancellationToken.None);

        Assert.Equal(DeliveryStatus.Retry, result.Status);
        Assert.Equal("2", result.ErrorCode);
        Assert.Equal("Temporary failure.", result.ErrorMessage);
        Assert.Equal(TimeSpan.FromSeconds(45), result.RetryAfter);
    }

    [Fact]
    public async Task Meta_transient_flag_requests_retry_for_http_400()
    {
        var result = await CreateClient(new StubHttpHandler((_, _) => Task.FromResult(
                MetaError(HttpStatusCode.BadRequest, 1, "Meta transient failure.", isTransient: true))))
            .SendAsync(new InstagramReply("recipient-1", "Текст"), CancellationToken.None);

        Assert.Equal(DeliveryStatus.Retry, result.Status);
        Assert.Equal("1", result.ErrorCode);
        Assert.Equal("Meta transient failure.", result.ErrorMessage);
    }

    [Fact]
    public async Task Retry_after_http_date_is_converted_to_delay()
    {
        var response = MetaError(HttpStatusCode.TooManyRequests, 2, "Wait until specified date.");
        response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(
            Now.AddSeconds(75));

        var result = await CreateClient(new StubHttpHandler((_, _) => Task.FromResult(response)))
            .SendAsync(new InstagramReply("recipient-1", "Текст"), CancellationToken.None);

        Assert.Equal(DeliveryStatus.Retry, result.Status);
        Assert.Equal(TimeSpan.FromSeconds(75), result.RetryAfter);
    }

    [Fact]
    public async Task Permanent_http_error_preserves_original_code_and_message()
    {
        var result = await CreateClient(new StubHttpHandler((_, _) => Task.FromResult(
                MetaError(HttpStatusCode.BadRequest, 100, "Invalid recipient."))))
            .SendAsync(new InstagramReply("recipient-1", "Текст"), CancellationToken.None);

        Assert.Equal(DeliveryStatus.Failed, result.Status);
        Assert.Equal("100", result.ErrorCode);
        Assert.Equal("Invalid recipient.", result.ErrorMessage);
        Assert.Null(result.RetryAfter);
    }

    [Fact]
    public async Task Caller_cancellation_is_propagated()
    {
        var handler = new StubHttpHandler((_, cancellationToken) =>
            Task.FromCanceled<HttpResponseMessage>(cancellationToken));
        using var source = new CancellationTokenSource();
        source.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CreateClient(handler).SendAsync(
                new InstagramReply("recipient-1", "Текст"),
                source.Token));
    }

    [Fact]
    public async Task Network_failure_requests_retry_without_internal_repetition()
    {
        var calls = 0;
        var handler = new StubHttpHandler((_, _) =>
        {
            calls++;
            throw new HttpRequestException("Synthetic network failure.");
        });

        var result = await CreateClient(handler).SendAsync(
            new InstagramReply("recipient-1", "Текст"),
            CancellationToken.None);

        Assert.Equal(DeliveryStatus.Retry, result.Status);
        Assert.Equal(nameof(HttpRequestException), result.ErrorCode);
        Assert.Equal("Synthetic network failure.", result.ErrorMessage);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Oversized_error_response_does_not_read_unbounded_body()
    {
        var handler = new StubHttpHandler((_, _) => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StreamContent(new MemoryStream(new byte[65_537]))
            }));

        var result = await CreateClient(handler).SendAsync(
            new InstagramReply("recipient-1", "Текст"),
            CancellationToken.None);

        Assert.Equal(DeliveryStatus.Failed, result.Status);
        Assert.Equal("ResponseTooLarge", result.ErrorCode);
    }

    private static HttpResponseMessage MetaError(
        HttpStatusCode statusCode,
        int code,
        string message,
        bool isTransient = false) => new(statusCode)
    {
        Content = JsonContent.Create(new
        {
            error = new
            {
                message,
                type = "OAuthException",
                code,
                is_transient = isTransient,
                fbtrace_id = "synthetic-trace"
            }
        })
    };

    private static InstagramGraphApiClient CreateClient(HttpMessageHandler handler) => new(
        new HttpClient(handler) { BaseAddress = new Uri("https://graph.instagram.com/") },
        Options.Create(new InstagramOptions
        {
            AccountId = "account-1",
            AccessToken = "access-token",
            ApiVersion = "v26.0",
            SendTimeoutSeconds = 15
        }),
        new FixedTimeProvider());

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class StubHttpHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handle)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => handle(request, cancellationToken);
    }
}
