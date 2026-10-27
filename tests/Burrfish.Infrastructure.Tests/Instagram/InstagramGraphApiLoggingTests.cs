using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using Burrfish.Application.Instagram;
using Burrfish.Infrastructure.Instagram;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Burrfish.Infrastructure.Tests.Instagram;

public sealed class InstagramGraphApiLoggingTests
{
    [Fact]
    public async Task Http_logs_do_not_contain_token_message_or_integration_error()
    {
        const string tokenMarker = "synthetic-instagram-token-marker";
        const string textMarker = "synthetic-instagram-message-marker";
        const string errorMarker = "synthetic-meta-error-marker";
        var loggerProvider = new RecordingLoggerProvider();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Instagram:AccountId"] = "account-1",
                ["Instagram:AppSecret"] = "app-secret",
                ["Instagram:VerifyToken"] = "verify-token",
                ["Instagram:AccessToken"] = tokenMarker,
                ["Instagram:ApiVersion"] = "v26.0",
                ["Instagram:SendTimeoutSeconds"] = "15"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Trace);
            builder.AddProvider(loggerProvider);
        });
        services.AddInfrastructure(configuration);
        services.AddHttpClient<IInstagramMessageSender, InstagramGraphApiClient>()
            .ConfigurePrimaryHttpMessageHandler(() => new StubHttpHandler((_, _) => Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.BadRequest)
                {
                    Content = JsonContent.Create(new
                    {
                        error = new { code = 100, message = errorMarker }
                    })
                })));
        await using var provider = services.BuildServiceProvider();

        var result = await provider.GetRequiredService<IInstagramMessageSender>()
            .SendAsync(new InstagramReply("recipient-1", textMarker), CancellationToken.None);

        Assert.NotNull(result.ErrorCode);
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
