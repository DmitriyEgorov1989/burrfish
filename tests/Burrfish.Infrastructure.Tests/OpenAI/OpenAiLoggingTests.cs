using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using Burrfish.Application.OpenAI;
using Burrfish.Infrastructure.OpenAI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Burrfish.Infrastructure.Tests.OpenAI;

public sealed class OpenAiLoggingTests
{
    [Fact]
    public async Task Http_logs_do_not_contain_credentials_input_or_model_output()
    {
        const string apiKeyMarker = "synthetic-api-key-marker";
        const string inputMarker = "synthetic-user-input-marker";
        const string outputMarker = "synthetic-model-output-marker";
        var loggerProvider = new RecordingLoggerProvider();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OpenAI:ApiKey"] = apiKeyMarker,
                ["OpenAI:Model"] = "test-model",
                ["OpenAI:TimeoutSeconds"] = "15",
                ["Dialog:RetentionDays"] = "30",
                ["Dialog:PriceExamples:0"] = "Сколько стоит?",
                ["Dialog:ServicesExamples:0"] = "Какие услуги?",
                ["Dialog:PriceResponse"] = "Ответ о цене.",
                ["Dialog:ServicesResponse"] = "Ответ об услугах.",
                ["Dialog:FallbackResponse"] = "Уточните задачу.",
                ["Dialog:HandoffOffer"] = "Передать менеджеру?",
                ["Dialog:HandoffConfirmationPrompt"] = "Передать запрос: {need}?",
                ["Dialog:AmbiguousConfirmationResponse"] = "Ответьте да или нет."
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Trace);
            builder.AddProvider(loggerProvider);
        });
        services.AddInfrastructure(configuration);
        services.AddHttpClient<IAiDialogClient, OpenAiDialogClient>()
            .ConfigurePrimaryHttpMessageHandler(() => new StubHttpHandler((_, _) =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
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
                                        text = $$"""
                                            { "kind": "ask_clarifying_question", "text": "{{outputMarker}}" }
                                            """
                                    }
                                }
                            }
                        }
                    })
                })));
        await using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IAiDialogClient>();

        var result = await client.DecideAsync(
            new AiDialogRequest(inputMarker, []),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        var logs = string.Join(Environment.NewLine, loggerProvider.Messages);
        Assert.DoesNotContain(apiKeyMarker, logs, StringComparison.Ordinal);
        Assert.DoesNotContain(inputMarker, logs, StringComparison.Ordinal);
        Assert.DoesNotContain(outputMarker, logs, StringComparison.Ordinal);
    }

    private sealed class RecordingLoggerProvider : ILoggerProvider
    {
        public ConcurrentQueue<string> Messages { get; } = new();

        public ILogger CreateLogger(string categoryName) => new RecordingLogger(Messages);

        public void Dispose()
        {
        }

        private sealed class RecordingLogger(ConcurrentQueue<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter) =>
                messages.Enqueue(formatter(state, exception));
        }
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
}
