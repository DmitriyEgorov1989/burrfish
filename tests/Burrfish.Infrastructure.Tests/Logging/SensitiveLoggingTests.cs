using Burrfish.Infrastructure.Configuration;
using Burrfish.Infrastructure.Persistence;
using Burrfish.Infrastructure.Processing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Burrfish.Infrastructure.Tests.Logging;

public sealed class SensitiveLoggingTests
{
    [Fact]
    public async Task Retention_worker_does_not_log_exception_message()
    {
        const string sensitiveMarker = "message-and-token-sensitive-marker";
        var services = new ServiceCollection();
        services.AddScoped<DataRetentionService>(_ =>
            throw new InvalidOperationException(sensitiveMarker));
        await using var provider = services.BuildServiceProvider();
        var logger = new RecordingLogger<DataRetentionWorker>();
        var heartbeat = new WorkerHeartbeat(TimeProvider.System);
        var worker = new DataRetentionWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new DataRetentionOptions { Enabled = true, IntervalMinutes = 1 }),
            TimeProvider.System,
            heartbeat,
            logger);

        await worker.StartAsync(CancellationToken.None);
        await logger.Logged.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await worker.StopAsync(CancellationToken.None);

        Assert.DoesNotContain(sensitiveMarker, string.Join(Environment.NewLine, logger.Messages));
        Assert.Contains(logger.Messages, message => message.Contains(
            nameof(InvalidOperationException),
            StringComparison.Ordinal));
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];
        public TaskCompletionSource Logged { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
            if (exception is not null)
            {
                Messages.Add(exception.ToString());
            }
            Logged.TrySetResult();
        }
    }
}
