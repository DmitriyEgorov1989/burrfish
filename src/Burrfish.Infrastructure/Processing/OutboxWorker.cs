using Burrfish.Application.Instagram;
using Burrfish.Application.Processing;
using Burrfish.Application.Telegram;
using Burrfish.Infrastructure.Configuration;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Burrfish.Infrastructure.Processing;

/// <summary>Запускает доставку outbox отдельными scoped-попытками.</summary>
public sealed class OutboxWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<OutboxProcessingOptions> options,
    TimeProvider timeProvider,
    ILogger<OutboxWorker> logger) : BackgroundService
{
    /// <inheritdoc />
    public override Task StartAsync(CancellationToken cancellationToken)
    {
        if (options.Value.Enabled)
        {
            using var scope = scopeFactory.CreateScope();
            var handler = scope.ServiceProvider.GetService<
                IRequestHandler<SendInstagramReplyCommand, DeliveryResult>>();
            if (handler is null)
            {
                throw new InvalidOperationException(
                    "Outbox processing requires SendInstagramReplyCommand handler.");
            }

            var telegramHandler = scope.ServiceProvider.GetService<
                IRequestHandler<SendLeadNotificationCommand, DeliveryResult>>();
            if (telegramHandler is null)
            {
                throw new InvalidOperationException(
                    "Outbox processing requires SendLeadNotificationCommand handler.");
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
            var processed = false;
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                processed = await scope.ServiceProvider
                    .GetRequiredService<OutboxDispatcher>()
                    .DispatchNextAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    "Outbox processing failed. ErrorType: {ErrorType}",
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
