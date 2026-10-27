using System.Net;
using System.Net.Http.Json;
using Burrfish.Application.OpenAI;
using Burrfish.Infrastructure.Configuration;
using Burrfish.Infrastructure.OpenAI;
using Microsoft.Extensions.Options;

namespace Burrfish.Infrastructure.Tests.OpenAI;

public sealed class OpenAiDialogClientTests
{
    [Fact]
    public async Task Completed_response_returns_validated_decision()
    {
        HttpRequestMessage? capturedRequest = null;
        var handler = new StubHttpHandler((request, _) =>
        {
            capturedRequest = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
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
                                new
                                {
                                    type = "output_text",
                                    text = "{\"kind\":\"price\",\"text\":null}"
                                }
                            }
                        }
                    }
                })
            });
        });
        var client = CreateClient(handler);

        var result = await client.DecideAsync(
            new AiDialogRequest("Сколько стоит?", []),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(AiDialogDecisionKind.Price, result.Decision!.Kind);
        Assert.Equal(HttpMethod.Post, capturedRequest!.Method);
        Assert.Equal("https://api.openai.com/v1/responses", capturedRequest.RequestUri!.AbsoluteUri);
        Assert.Equal("Bearer", capturedRequest.Headers.Authorization!.Scheme);
        Assert.Equal("test-api-key", capturedRequest.Headers.Authorization.Parameter);
    }

    [Fact]
    public async Task Reasoning_output_before_message_is_ignored()
    {
        var handler = new StubHttpHandler((_, _) => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    status = "completed",
                    output = new object[]
                    {
                        new { type = "reasoning", id = "synthetic-reasoning" },
                        new
                        {
                            type = "message",
                            role = "assistant",
                            content = new[]
                            {
                                new
                                {
                                    type = "output_text",
                                    text = "{\"kind\":\"services\",\"text\":null}"
                                }
                            }
                        }
                    }
                })
            }));
        var client = CreateClient(handler);

        var result = await client.DecideAsync(
            new AiDialogRequest("Какие услуги?", []),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(AiDialogDecisionKind.Services, result.Decision!.Kind);
    }

    [Fact]
    public async Task Invalid_decision_preserves_successful_http_status()
    {
        var handler = new StubHttpHandler((_, _) => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.OK)
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
                                new { type = "output_text", text = "{}" }
                            }
                        }
                    }
                })
            }));
        var client = CreateClient(handler);

        var result = await client.DecideAsync(
            new AiDialogRequest("Синтетический вопрос", []),
            CancellationToken.None);

        Assert.Equal("InvalidDecision", result.Error!.Code);
        Assert.Equal(200, result.Error.HttpStatusCode);
    }

    [Fact]
    public async Task Http_error_preserves_original_code_message_and_status()
    {
        var requestCount = 0;
        var handler = new StubHttpHandler((_, _) =>
        {
            requestCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            {
                Content = JsonContent.Create(new
                {
                    error = new
                    {
                        code = "rate_limit_exceeded",
                        type = "rate_limit_error",
                        message = "Synthetic rate limit response."
                    }
                })
            });
        });
        var client = CreateClient(handler);

        var result = await client.DecideAsync(
            new AiDialogRequest("Синтетический вопрос", []),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("rate_limit_exceeded", result.Error!.Code);
        Assert.Equal("Synthetic rate limit response.", result.Error.Message);
        Assert.Equal(429, result.Error.HttpStatusCode);
        Assert.True(result.Error.IsTransient);
        Assert.Equal(1, requestCount);
    }

    [Fact]
    public async Task Billing_limit_is_not_marked_transient()
    {
        var handler = new StubHttpHandler((_, _) => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            {
                Content = JsonContent.Create(new
                {
                    error = new
                    {
                        code = "insufficient_quota",
                        type = "insufficient_quota",
                        message = "Synthetic quota response."
                    }
                })
            }));
        var client = CreateClient(handler);

        var result = await client.DecideAsync(
            new AiDialogRequest("Синтетический вопрос", []),
            CancellationToken.None);

        Assert.False(result.Error!.IsTransient);
    }

    [Theory]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task Temporary_http_status_is_marked_transient(HttpStatusCode statusCode)
    {
        var handler = new StubHttpHandler((_, _) => Task.FromResult(
            new HttpResponseMessage(statusCode)
            {
                Content = JsonContent.Create(new
                {
                    error = new
                    {
                        code = "synthetic_temporary_error",
                        message = "Synthetic temporary response."
                    }
                })
            }));
        var client = CreateClient(handler);

        var result = await client.DecideAsync(
            new AiDialogRequest("Синтетический вопрос", []),
            CancellationToken.None);

        Assert.Equal((int)statusCode, result.Error!.HttpStatusCode);
        Assert.True(result.Error.IsTransient);
    }

    [Theory]
    [InlineData("incomplete", "IncompleteResponse")]
    [InlineData("failed", "OpenAiHttpError")]
    public async Task Non_completed_response_returns_failure(
        string status,
        string expectedCode)
    {
        var handler = new StubHttpHandler((_, _) => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    status,
                    output = Array.Empty<object>()
                })
            }));
        var client = CreateClient(handler);

        var result = await client.DecideAsync(
            new AiDialogRequest("Синтетический вопрос", []),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(expectedCode, result.Error!.Code);
    }

    [Fact]
    public async Task Model_refusal_returns_failure_without_refusal_text()
    {
        const string refusalText = "Synthetic refusal details.";
        var handler = new StubHttpHandler((_, _) => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.OK)
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
                                new { type = "refusal", refusal = refusalText }
                            }
                        }
                    }
                })
            }));
        var client = CreateClient(handler);

        var result = await client.DecideAsync(
            new AiDialogRequest("Синтетический вопрос", []),
            CancellationToken.None);

        Assert.Equal("ModelRefusal", result.Error!.Code);
        Assert.DoesNotContain(refusalText, result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Invalid_input_does_not_send_http_request()
    {
        var requestCount = 0;
        var handler = new StubHttpHandler((_, _) =>
        {
            requestCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });
        var client = CreateClient(handler);

        var result = await client.DecideAsync(
            new AiDialogRequest(new string('a', 4_001), []),
            CancellationToken.None);

        Assert.Equal("InvalidInput", result.Error!.Code);
        Assert.Equal(0, requestCount);
    }

    [Fact]
    public async Task Oversized_response_returns_failure()
    {
        var handler = new StubHttpHandler((_, _) => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(new MemoryStream(new byte[65_537]))
            }));
        var client = CreateClient(handler);

        var result = await client.DecideAsync(
            new AiDialogRequest("Синтетический вопрос", []),
            CancellationToken.None);

        Assert.Equal("ResponseTooLarge", result.Error!.Code);
    }

    [Fact]
    public async Task Oversized_server_error_is_transient()
    {
        var handler = new StubHttpHandler((_, _) => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                Content = new StreamContent(new MemoryStream(new byte[65_537]))
            }));
        var client = CreateClient(handler);

        var result = await client.DecideAsync(
            new AiDialogRequest("Синтетический вопрос", []),
            CancellationToken.None);

        Assert.Equal(503, result.Error!.HttpStatusCode);
        Assert.True(result.Error.IsTransient);
    }

    [Fact]
    public async Task Body_read_failure_preserves_received_http_status()
    {
        var handler = new StubHttpHandler((_, _) => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ThrowingContent()
            }));
        var client = CreateClient(handler);

        var result = await client.DecideAsync(
            new AiDialogRequest("Синтетический вопрос", []),
            CancellationToken.None);

        Assert.Equal(200, result.Error!.HttpStatusCode);
        Assert.True(result.Error.IsTransient);
    }

    [Fact]
    public async Task Timeout_returns_transient_failure()
    {
        var handler = new StubHttpHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var client = CreateClient(handler, timeoutSeconds: 1);

        var result = await client.DecideAsync(
            new AiDialogRequest("Синтетический вопрос", []),
            CancellationToken.None);

        Assert.Equal("Timeout", result.Error!.Code);
        Assert.True(result.Error.IsTransient);
        Assert.Null(result.Error.HttpStatusCode);
    }

    [Fact]
    public async Task Caller_cancellation_is_propagated()
    {
        var handler = new StubHttpHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        var client = CreateClient(handler);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.DecideAsync(
                new AiDialogRequest("Синтетический вопрос", []),
                cancellation.Token));
    }

    private static OpenAiDialogClient CreateClient(
        HttpMessageHandler handler,
        int timeoutSeconds = 15)
    {
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.openai.com/v1/"),
            Timeout = Timeout.InfiniteTimeSpan
        };
        var options = Options.Create(new OpenAiOptions
        {
            ApiKey = "test-api-key",
            Model = "test-model",
            TimeoutSeconds = timeoutSeconds
        });
        var requestBuilder = new OpenAiRequestBuilder(Options.Create(new DialogOptions
        {
            PriceExamples = ["Сколько стоит?"],
            ServicesExamples = ["Какие услуги?"]
        }));

        return new OpenAiDialogClient(
            httpClient,
            options,
            requestBuilder,
            new OpenAiResponseReader(new OpenAiResponseValidator()));
    }

    private sealed class StubHttpHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handle)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            handle(request, cancellationToken);
    }

    private sealed class ThrowingContent : HttpContent
    {
        protected override Task SerializeToStreamAsync(
            Stream stream,
            TransportContext? context) =>
            Task.FromException(new IOException("Synthetic body read failure."));

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }
}
