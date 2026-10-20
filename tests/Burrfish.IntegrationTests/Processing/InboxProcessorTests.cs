using System.Text.Json;
using Burrfish.Application;
using Burrfish.Application.Instagram;
using Burrfish.Application.Persistence;
using Burrfish.Infrastructure.Configuration;
using Burrfish.Infrastructure.Instagram;
using Burrfish.Infrastructure.Persistence;
using Burrfish.Infrastructure.Persistence.Entities;
using Burrfish.Infrastructure.Processing;
using Burrfish.IntegrationTests.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Burrfish.IntegrationTests.Processing;

[Collection(PostgresCollection.Name)]
public sealed class InboxProcessorTests(PostgresFixture fixture)
{
    private const string AccountId = "processor-account";
    private static readonly DateTimeOffset Now =
        new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Successful_message_is_processed_once()
    {
        await ClearQueueAsync();
        var eventId = Guid.NewGuid().ToString("N");
        await SeedAsync(eventId, CreatePayload(eventId));
        var probe = new HandlerProbe(HandlerMode.Success);
        await using var provider = CreateProvider(probe);

        await using (var scope = provider.CreateAsyncScope())
        {
            var processor = scope.ServiceProvider.GetRequiredService<InboxProcessor>();
            Assert.True(await processor.ProcessNextAsync(CancellationToken.None));
            Assert.False(await processor.ProcessNextAsync(CancellationToken.None));
        }

        Assert.Equal(1, probe.Calls);
        await using var db = fixture.CreateContext();
        var stored = await db.InboxEvents.SingleAsync(item => item.ExternalEventId == eventId);
        Assert.Equal(InboxEventStatus.Processed, stored.Status);
        Assert.Equal(1, stored.AttemptCount);
        Assert.Null(stored.LastErrorCode);
    }

    [Fact]
    public async Task Unsupported_event_is_ignored_without_handler()
    {
        await ClearQueueAsync();
        var eventId = Guid.NewGuid().ToString("N");
        await SeedAsync(eventId, "{");
        var probe = new HandlerProbe(HandlerMode.Success);
        await using var provider = CreateProvider(probe);

        await using (var scope = provider.CreateAsyncScope())
        {
            await scope.ServiceProvider
                .GetRequiredService<InboxProcessor>()
                .ProcessNextAsync(CancellationToken.None);
        }

        Assert.Equal(0, probe.Calls);
        await using var db = fixture.CreateContext();
        var stored = await db.InboxEvents.SingleAsync(item => item.ExternalEventId == eventId);
        Assert.Equal(InboxEventStatus.Ignored, stored.Status);
        Assert.Equal("UnsupportedOrMalformedEvent", stored.LastErrorCode);
    }

    [Fact]
    public async Task Retry_rolls_back_handler_changes_and_sets_next_attempt()
    {
        await ClearQueueAsync();
        var eventId = Guid.NewGuid().ToString("N");
        await SeedAsync(eventId, CreatePayload(eventId));
        var probe = new HandlerProbe(HandlerMode.RetryWithOutbox);
        await using var provider = CreateProvider(probe);

        await using (var scope = provider.CreateAsyncScope())
        {
            await scope.ServiceProvider
                .GetRequiredService<InboxProcessor>()
                .ProcessNextAsync(CancellationToken.None);
        }

        await using var db = fixture.CreateContext();
        var stored = await db.InboxEvents.SingleAsync(item => item.ExternalEventId == eventId);
        Assert.Equal(InboxEventStatus.Pending, stored.Status);
        Assert.Equal(1, stored.AttemptCount);
        Assert.Equal(Now.AddSeconds(30), stored.NextAttemptAt);
        Assert.Equal("TemporaryFailure", stored.LastErrorCode);
        Assert.Equal(0, await db.OutboxMessages.CountAsync());
    }

    [Fact]
    public async Task Concurrent_processor_skips_locked_event()
    {
        await ClearQueueAsync();
        var eventId = Guid.NewGuid().ToString("N");
        await SeedAsync(eventId, CreatePayload(eventId));
        var probe = new HandlerProbe(HandlerMode.Block);
        await using var provider = CreateProvider(probe);
        await using var firstScope = provider.CreateAsyncScope();
        await using var secondScope = provider.CreateAsyncScope();

        var first = firstScope.ServiceProvider
            .GetRequiredService<InboxProcessor>()
            .ProcessNextAsync(CancellationToken.None);
        await probe.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var second = await secondScope.ServiceProvider
            .GetRequiredService<InboxProcessor>()
            .ProcessNextAsync(CancellationToken.None);
        Assert.False(second);

        probe.Release.TrySetResult(true);
        Assert.True(await first.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(1, probe.Calls);
    }

    [Fact]
    public async Task Handler_exception_schedules_retry_without_starving_queue()
    {
        await ClearQueueAsync();
        var firstId = $"a-{Guid.NewGuid():N}";
        var secondId = $"b-{Guid.NewGuid():N}";
        await SeedAsync(firstId, CreatePayload(firstId), Now.AddSeconds(-1));
        await SeedAsync(secondId, CreatePayload(secondId));
        var probe = new HandlerProbe(HandlerMode.ThrowOnce);
        await using var provider = CreateProvider(probe);

        await using (var scope = provider.CreateAsyncScope())
        {
            var processor = scope.ServiceProvider.GetRequiredService<InboxProcessor>();
            Assert.True(await processor.ProcessNextAsync(CancellationToken.None));
            Assert.True(await processor.ProcessNextAsync(CancellationToken.None));
        }

        await using var db = fixture.CreateContext();
        var first = await db.InboxEvents.SingleAsync(item => item.ExternalEventId == firstId);
        var second = await db.InboxEvents.SingleAsync(item => item.ExternalEventId == secondId);
        Assert.Equal(InboxEventStatus.Pending, first.Status);
        Assert.Equal(Now.AddSeconds(30), first.NextAttemptAt);
        Assert.Equal(nameof(TimeoutException), first.LastErrorCode);
        Assert.Equal(InboxEventStatus.Processed, second.Status);
    }

    private ServiceProvider CreateProvider(HandlerProbe probe)
    {
        var services = new ServiceCollection();
        services.AddApplication();
        services.AddSingleton(probe);
        services.AddTransient<
            IRequestHandler<ProcessIncomingMessageCommand, ProcessIncomingMessageResult>,
            ProbeHandler>();
        services.AddSingleton<IOptions<InstagramOptions>>(
            Options.Create(new InstagramOptions { AccountId = AccountId }));
        services.AddSingleton<IOptions<InboxProcessingOptions>>(
            Options.Create(new InboxProcessingOptions()));
        services.AddSingleton<TimeProvider>(new FixedTimeProvider(Now));
        services.AddSingleton<InstagramEventNormalizer>();
        services.AddDbContext<BurrfishDbContext>(options =>
            options.UseNpgsql(fixture.ConnectionString)
                .EnableSensitiveDataLogging(false));
        services.AddScoped<InboxProcessor>();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    private async Task ClearQueueAsync()
    {
        await using var db = fixture.CreateContext();
        await db.OutboxMessages.ExecuteDeleteAsync();
        await db.InboxEvents.ExecuteDeleteAsync();
    }

    private async Task SeedAsync(
        string eventId,
        string payload,
        DateTimeOffset? availableAt = null)
    {
        await using var db = fixture.CreateContext();
        db.InboxEvents.Add(new InboxEvent
        {
            Id = Guid.NewGuid(),
            ExternalEventId = eventId,
            Payload = payload,
            ReceivedAt = availableAt ?? Now,
            Status = InboxEventStatus.Pending,
            NextAttemptAt = availableAt ?? Now
        });
        await db.SaveChangesAsync();
    }

    private static string CreatePayload(string eventId) => JsonSerializer.Serialize(new
    {
        @object = "instagram",
        entry = new[]
        {
            new
            {
                id = AccountId,
                messaging = new[]
                {
                    new
                    {
                        sender = new { id = "sender-1" },
                        recipient = new { id = AccountId },
                        timestamp = Now.ToUnixTimeMilliseconds(),
                        message = new { mid = eventId, text = "Тест" }
                    }
                }
            }
        }
    });

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private enum HandlerMode
    {
        Success,
        RetryWithOutbox,
        Block,
        ThrowOnce
    }

    private sealed class HandlerProbe(HandlerMode mode)
    {
        public HandlerMode Mode { get; } = mode;
        public int Calls;
        public TaskCompletionSource<bool> Entered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class ProbeHandler(HandlerProbe probe, BurrfishDbContext db)
        : IRequestHandler<ProcessIncomingMessageCommand, ProcessIncomingMessageResult>
    {
        public async Task<ProcessIncomingMessageResult> Handle(
            ProcessIncomingMessageCommand request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref probe.Calls);

            if (probe.Mode == HandlerMode.ThrowOnce && probe.Calls == 1)
            {
                throw new TimeoutException();
            }

            if (probe.Mode == HandlerMode.Block)
            {
                probe.Entered.TrySetResult(true);
                await probe.Release.Task.WaitAsync(cancellationToken);
            }

            if (probe.Mode == HandlerMode.RetryWithOutbox)
            {
                new OutboxWriter(db).Add(new OutboxEnvelope(
                    Guid.NewGuid(),
                    $"test:{request.Message.ExternalEventId}",
                    "test",
                    "{}",
                    Now));
                await db.SaveChangesAsync(cancellationToken);
                return new(false, "TemporaryFailure");
            }

            return new(true, null);
        }
    }
}
