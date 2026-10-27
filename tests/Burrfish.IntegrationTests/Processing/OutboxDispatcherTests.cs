using System.Text.Json;
using Burrfish.Application;
using Burrfish.Application.Instagram;
using Burrfish.Application.Persistence;
using Burrfish.Application.Processing;
using Burrfish.Infrastructure.Configuration;
using Burrfish.Infrastructure.Persistence;
using Burrfish.Infrastructure.Persistence.Entities;
using Burrfish.Infrastructure.Processing;
using Burrfish.IntegrationTests.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Burrfish.IntegrationTests.Processing;

[Collection(PostgresCollection.Name)]
public sealed class OutboxDispatcherTests(PostgresFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 13, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Sent_item_is_not_selected_again_and_telegram_item_is_ignored()
    {
        await ClearAsync();
        var instagramId = await SeedAsync("instagram.reply.v1", ReplyPayload());
        await SeedAsync("telegram.lead-created.v1", JsonSerializer.Serialize(new { LeadId = Guid.NewGuid() }));
        var sender = new StubSender(Delivery(DeliveryStatus.Sent));
        await using var provider = CreateProvider(sender);

        Assert.True(await DispatchAsync(provider));
        Assert.False(await DispatchAsync(provider));

        Assert.Equal(1, sender.Calls);
        Assert.Equal("recipient-1", sender.LastReply!.RecipientId);
        Assert.Equal("Ответ клиенту", sender.LastReply.Text);
        await using var db = fixture.CreateContext();
        var sent = await db.OutboxMessages.SingleAsync(item => item.Id == instagramId);
        Assert.Equal(OutboxMessageStatus.Sent, sent.Status);
        Assert.Equal(1, sent.AttemptCount);
        Assert.Null(sent.LastErrorCode);
        Assert.Equal(
            OutboxMessageStatus.Pending,
            (await db.OutboxMessages.SingleAsync(item => item.Type == "telegram.lead-created.v1")).Status);
    }

    [Fact]
    public async Task Retry_preserves_error_and_uses_longer_retry_after()
    {
        await ClearAsync();
        var id = await SeedAsync("instagram.reply.v1", ReplyPayload());
        var clock = new MutableTimeProvider();
        var sender = new StubSender(new DeliveryResult(
            DeliveryStatus.Retry,
            null,
            "2",
            "Temporary Meta failure.",
            TimeSpan.FromSeconds(45)), () => clock.UtcNow = Now.AddSeconds(60));
        await using var provider = CreateProvider(sender, clock);

        Assert.True(await DispatchAsync(provider));
        Assert.False(await DispatchAsync(provider));

        await using var db = fixture.CreateContext();
        var pending = await db.OutboxMessages.SingleAsync(item => item.Id == id);
        Assert.Equal(OutboxMessageStatus.Pending, pending.Status);
        Assert.Equal(1, pending.AttemptCount);
        Assert.Equal(Now.AddSeconds(105), pending.NextAttemptAt);
        Assert.Equal("2", pending.LastErrorCode);
        Assert.Equal("Temporary Meta failure.", pending.LastErrorMessage);
    }

    [Fact]
    public async Task Permanent_error_and_malformed_payload_fail_without_reselection()
    {
        await ClearAsync();
        var permanentId = await SeedAsync("instagram.reply.v1", ReplyPayload(), Now.AddMilliseconds(-1));
        var malformedId = await SeedAsync("instagram.reply.v1", "{}", Now);
        var sender = new StubSender(new DeliveryResult(
            DeliveryStatus.Failed,
            null,
            "100",
            "Invalid recipient.",
            null));
        await using var provider = CreateProvider(sender);

        Assert.True(await DispatchAsync(provider));
        Assert.True(await DispatchAsync(provider));
        Assert.False(await DispatchAsync(provider));

        Assert.Equal(1, sender.Calls);
        await using var db = fixture.CreateContext();
        var permanent = await db.OutboxMessages.SingleAsync(item => item.Id == permanentId);
        Assert.Equal(OutboxMessageStatus.Failed, permanent.Status);
        Assert.Equal("100", permanent.LastErrorCode);
        Assert.Equal("Invalid recipient.", permanent.LastErrorMessage);
        var malformed = await db.OutboxMessages.SingleAsync(item => item.Id == malformedId);
        Assert.Equal(OutboxMessageStatus.Failed, malformed.Status);
        Assert.Equal("InvalidOutboxPayload", malformed.LastErrorCode);
    }

    [Fact]
    public async Task Concurrent_dispatcher_skips_locked_item()
    {
        await ClearAsync();
        await SeedAsync("instagram.reply.v1", ReplyPayload());
        var sender = new BlockingSender();
        await using var provider = CreateProvider(sender);
        await using var firstScope = provider.CreateAsyncScope();
        await using var secondScope = provider.CreateAsyncScope();

        var first = firstScope.ServiceProvider.GetRequiredService<OutboxDispatcher>()
            .DispatchNextAsync(CancellationToken.None);
        await sender.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = await secondScope.ServiceProvider.GetRequiredService<OutboxDispatcher>()
            .DispatchNextAsync(CancellationToken.None);

        Assert.False(second);
        sender.Release.TrySetResult();
        Assert.True(await first.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(1, sender.Calls);
    }

    private ServiceProvider CreateProvider(
        IInstagramMessageSender sender,
        TimeProvider? timeProvider = null)
    {
        var services = new ServiceCollection();
        services.AddApplication();
        services.AddSingleton(sender);
        services.AddSingleton<IInstagramMessageSender>(sender);
        services.AddSingleton(timeProvider ?? new FixedTimeProvider());
        services.AddSingleton<IOptions<OutboxProcessingOptions>>(Options.Create(
            new OutboxProcessingOptions
            {
                RetryBaseDelaySeconds = 30,
                MaxRetryDelaySeconds = 900
            }));
        services.AddDbContext<BurrfishDbContext>(options => options.UseNpgsql(fixture.ConnectionString));
        services.AddScoped<OutboxDispatcher>();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    private static async Task<bool> DispatchAsync(ServiceProvider provider)
    {
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<OutboxDispatcher>()
            .DispatchNextAsync(CancellationToken.None);
    }

    private async Task ClearAsync()
    {
        await using var db = fixture.CreateContext();
        await db.OutboxMessages.ExecuteDeleteAsync();
    }

    private async Task<Guid> SeedAsync(string type, string payload, DateTimeOffset? createdAt = null)
    {
        var id = Guid.NewGuid();
        await using var db = fixture.CreateContext();
        new OutboxWriter(db).Add(new OutboxEnvelope(
            id,
            $"{type}:{id:D}",
            type,
            payload,
            createdAt ?? Now));
        await db.SaveChangesAsync();
        return id;
    }

    private static string ReplyPayload() => JsonSerializer.Serialize(new
    {
        recipientId = "recipient-1",
        text = "Ответ клиенту"
    });

    private static DeliveryResult Delivery(DeliveryStatus status) =>
        new(status, "message-1", null, null, null);

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class MutableTimeProvider : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = Now;
        public override DateTimeOffset GetUtcNow() => UtcNow;
    }

    private sealed class StubSender(DeliveryResult result, Action? onSend = null) : IInstagramMessageSender
    {
        public int Calls { get; private set; }
        public InstagramReply? LastReply { get; private set; }

        public Task<DeliveryResult> SendAsync(InstagramReply reply, CancellationToken cancellationToken)
        {
            Calls++;
            LastReply = reply;
            onSend?.Invoke();
            return Task.FromResult(result);
        }
    }

    private sealed class BlockingSender : IInstagramMessageSender
    {
        public int Calls { get; private set; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<DeliveryResult> SendAsync(InstagramReply reply, CancellationToken cancellationToken)
        {
            Calls++;
            Entered.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return Delivery(DeliveryStatus.Sent);
        }
    }
}
