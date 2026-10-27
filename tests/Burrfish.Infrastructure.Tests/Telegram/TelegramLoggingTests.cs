using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using Burrfish.Application.Telegram;
using Burrfish.Infrastructure.Telegram;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Burrfish.Infrastructure.Tests.Telegram;

public sealed class TelegramLoggingTests
{
    [Fact]
    public async Task Logs_do_not_contain_token_message_or_integration_error()
    {
        const string tokenMarker = "synthetic-telegram-token-marker";
        const string textMarker = "synthetic-lead-need-marker";
        const string errorMarker = "synthetic-telegram-error-marker";
        var loggerProvider = new RecordingLoggerProvider();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Telegram:BotToken"] = tokenMarker,
                ["Telegram:ChatId"] = "-1001234567890",
                ["Telegram:TimeoutSeconds"] = "15"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Trace);
            builder.AddProvider(loggerProvider);
        });
        services.AddInfrastructure(configuration);
        services.AddHttpClient<ITelegramNotifier, TelegramNotifier>()
            .ConfigurePrimaryHttpMessageHandler(() => new StubHttpHandler((_, _) => Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.Forbidden)
                {
                    Content = JsonContent.Create(new
                    {
                        ok = false,
                        error_code = 403,
                        description = errorMarker
                    })
                })));
        await using var provider = services.BuildServiceProvider();

        var result = await provider.GetRequiredService<ITelegramNotifier>().SendLeadAsync(
            new LeadNotification(Guid.NewGuid(), "sender-1", null, textMarker, DateTimeOffset.UtcNow),
            CancellationToken.None);

        Assert.Equal(errorMarker, result.ErrorMessage);
        var logs = string.Join(Environment.NewLine, loggerProvider.Messages);
        Assert.DoesNotContain(tokenMarker, logs, StringComparison.Ordinal);
        Assert.DoesNotContain(textMarker, logs, StringComparison.Ordinal);
        Assert.DoesNotContain(errorMarker, logs, StringComparison.Ordinal);
    }

    private sealed class RecordingLoggerProvider : ILoggerProvider
    {
        public ConcurrentQueue<string> Messages { get; } = new();
        public ILogger CreateLogger(string categoryName) => new RecordingLogger(Messages);
        public void Dispose() { }

        private sealed class RecordingLogger(ConcurrentQueue<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
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
            CancellationToken cancellationToken) => handle(request, cancellationToken);
    }
}
