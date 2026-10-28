using Burrfish.Application.Instagram;
using Burrfish.Infrastructure.Configuration;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Burrfish.Infrastructure.Processing;

/// <summary>Запускает обработку inbox отдельными scoped-попытками.</summary>
public sealed class InboxWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<InboxProcessingOptions> options,
    TimeProvider timeProvider,
    ILogger<InboxWorker> logger,
    WorkerHeartbeat? heartbeat = null) : BackgroundService
{
    /// <inheritdoc />
    public override Task StartAsync(CancellationToken cancellationToken)
    {
        if (options.Value.Enabled)
        {
            using var scope = scopeFactory.CreateScope();
            var handler = scope.ServiceProvider.GetService<
                IRequestHandler<ProcessIncomingMessageCommand, ProcessIncomingMessageResult>>();

            if (handler is null)
            {
                throw new InvalidOperationException(
                    "Inbox processing requires ProcessIncomingMessageCommand handler.");
            }
        }

        return base.StartAsync(cancellationToken);
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            return;
        }

        var pause = TimeSpan.FromMilliseconds(options.Value.PollIntervalMilliseconds);
        while (!stoppingToken.IsCancellationRequested)
        {
            heartbeat?.Record(WorkerKind.Inbox);
            var processed = false;
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var processor = scope.ServiceProvider.GetRequiredService<InboxProcessor>();
                processed = await processor.ProcessNextAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    "Inbox processing failed. ErrorType: {ErrorType}",
                    exception.GetType().Name);
            }

            if (!processed)
            {
                try
                {
                    await Task.Delay(pause, timeProvider, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
            }
        }
    }
}
