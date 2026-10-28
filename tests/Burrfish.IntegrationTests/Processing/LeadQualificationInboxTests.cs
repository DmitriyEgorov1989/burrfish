using System.Text.Json;
using Burrfish.Application;
using Burrfish.Application.LeadQualification;
using Burrfish.Application.OpenAI;
using Burrfish.Application.Persistence;
using Burrfish.Domain.Conversations;
using Burrfish.Infrastructure.Configuration;
using Burrfish.Infrastructure.Instagram;
using Burrfish.Infrastructure.LeadQualification;
using Burrfish.Infrastructure.Persistence;
using Burrfish.Infrastructure.Persistence.Entities;
using Burrfish.Infrastructure.Processing;
using Burrfish.IntegrationTests.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Burrfish.IntegrationTests.Processing;

[Collection(PostgresCollection.Name)]
public sealed class LeadQualificationInboxTests(PostgresFixture fixture)
{
    private const string AccountId = "qualification-account";
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Question_confirmation_and_followup_produce_one_lead_and_no_post_handoff_reply(bool aiFailure)
    {
        await ClearQueueAsync();
        var sender = Guid.NewGuid().ToString("N");
        var ai = new TestAi(aiFailure);
        await using var provider = CreateProvider(ai);
        var questionId = await EnqueueAsync(sender, "Сколько стоит?");
        await ProcessAsync(provider);

        await using (var db = fixture.CreateContext())
        {
            var conversation = await db.Conversations.SingleAsync(c => c.SenderId == sender);
            Assert.Equal(ConversationState.AwaitingHandoffConfirmation, conversation.Model.State);
            Assert.Equal("Сколько стоит?", conversation.Model.PendingNeed);
            Assert.False(await db.Leads.AnyAsync(l => l.SenderId == sender));
            var reply = await db.OutboxMessages.SingleAsync();
            Assert.Equal($"instagram.reply.v1:{questionId}", reply.DeduplicationKey);
            using var payload = JsonDocument.Parse(reply.Payload);
            Assert.Equal(aiFailure ? "Уточнение недоступно.\n\nПередать менеджеру?" : "Ответ о цене.\n\nПередать менеджеру?",
                payload.RootElement.GetProperty("text").GetString());
        }

        var confirmationId = await EnqueueAsync(sender, "Да");
        await ProcessAsync(provider);
        Assert.False(await EnqueueDuplicateAsync(confirmationId));
        await EnqueueAsync(sender, "Ещё вопрос");
        await ProcessAsync(provider);

        await using var verificationDb = fixture.CreateContext();
        var stored = await verificationDb.Conversations.SingleAsync(c => c.SenderId == sender);
        Assert.Equal(ConversationState.OperatorHandoff, stored.Model.State);
        var lead = await verificationDb.Leads.SingleAsync(l => l.SenderId == sender);
        Assert.Equal("Сколько стоит?", lead.Need);
        Assert.Equal(Now, lead.CreatedAt);
        var notification = await verificationDb.OutboxMessages.SingleAsync(o => o.Type == "telegram.lead-created.v1");
        using var notificationPayload = JsonDocument.Parse(notification.Payload);
        Assert.Equal(lead.Id, notificationPayload.RootElement.GetProperty("LeadId").GetGuid());
        Assert.Equal(2, await verificationDb.OutboxMessages.CountAsync());
        Assert.Equal(3, await verificationDb.InboxEvents.CountAsync(i => i.Status == InboxEventStatus.Processed));
        Assert.Equal(1, ai.Calls);
    }

    [Fact]
    public async Task Retry_after_save_rolls_back_handoff_lead_and_notification()
    {
        await ClearQueueAsync();
        var sender = await SeedAwaitingAsync();
        var eventId = await EnqueueAsync(sender, "Да");
        await using var provider = CreateProvider(new TestAi(), failAfterSave: true);

        await ProcessAsync(provider);

        await using var db = fixture.CreateContext();
        var stored = await db.Conversations.SingleAsync(c => c.SenderId == sender);
        Assert.Equal(ConversationState.AwaitingHandoffConfirmation, stored.Model.State);
        Assert.Equal("Нужен проект", stored.Model.PendingNeed);
        Assert.False(await db.Leads.AnyAsync(l => l.SenderId == sender));
        Assert.Empty(await db.OutboxMessages.ToListAsync());
        var inbox = await db.InboxEvents.SingleAsync(i => i.ExternalEventId == eventId);
        Assert.Equal(InboxEventStatus.Pending, inbox.Status);
        Assert.Equal("InjectedRetry", inbox.LastErrorCode);
        Assert.Equal(Now.AddSeconds(30), inbox.NextAttemptAt);
    }

    [Fact]
    public async Task Message_after_expired_operator_handoff_starts_a_new_bot_active_conversation()
    {
        await ClearQueueAsync();
        var sender = Guid.NewGuid().ToString("N");
        var oldConversationId = Guid.NewGuid();
        var model = Conversation.Start();
        Assert.True(model.ProposeHandoff("Старый запрос").Succeeded);
        Assert.True(model.ConfirmHandoff(Now.AddDays(-31)).Succeeded);
        await using (var db = fixture.CreateContext())
        {
            new ConversationRepository(db).Add(new StoredConversation(
                oldConversationId,
                $"{AccountId}:{sender}",
                sender,
                null,
                Now.AddDays(-30),
                model));
            await db.SaveChangesAsync();
        }
        await using (var db = fixture.CreateContext())
        {
            await new DataRetentionService(
                    db,
                    new TestClock(),
                    Options.Create(new DialogOptions { RetentionDays = 30 }))
                .DeleteExpiredAsync(CancellationToken.None);
        }

        var ai = new TestAi(decisionKind: AiDialogDecisionKind.AskClarifyingQuestion);
        await using var provider = CreateProvider(ai);
        await EnqueueAsync(sender, "Новый вопрос");
        await ProcessAsync(provider);

        await using var verificationDb = fixture.CreateContext();
        var conversation = await verificationDb.Conversations.SingleAsync(x => x.SenderId == sender);
        Assert.NotEqual(oldConversationId, conversation.Id);
        Assert.Equal(ConversationState.BotActive, conversation.Model.State);
        Assert.Equal(1, ai.Calls);
    }

    [Fact]
    public async Task Concurrent_confirmations_create_one_lead_and_retry_loser_without_reply()
    {
        await ClearQueueAsync();
        var sender = await SeedAwaitingAsync();
        await EnqueueAsync(sender, "Да");
        await EnqueueAsync(sender, "Да");
        var gate = new ReadGate();
        var clock = new TestClock();
        await using var provider = CreateProvider(new TestAi(), gate: gate, clock: clock);

        await Task.WhenAll(ProcessAsync(provider), ProcessAsync(provider)).WaitAsync(TimeSpan.FromSeconds(20));

        await using (var db = fixture.CreateContext())
        {
            Assert.Equal(1, await db.Leads.CountAsync(l => l.SenderId == sender));
            Assert.Equal(1, await db.OutboxMessages.CountAsync());
            var pending = await db.InboxEvents.SingleAsync(i => i.Status == InboxEventStatus.Pending);
            Assert.Contains(pending.LastErrorCode, new[] { "ConcurrencyConflict", "PersistenceConflict" });
        }

        clock.UtcNow = Now.AddSeconds(30);
        await ProcessAsync(provider);

        await using var verificationDb = fixture.CreateContext();
        Assert.Equal(2, await verificationDb.InboxEvents.CountAsync(i => i.Status == InboxEventStatus.Processed));
        Assert.Equal(1, await verificationDb.Leads.CountAsync(l => l.SenderId == sender));
        Assert.Equal(1, await verificationDb.OutboxMessages.CountAsync());
    }

    private ServiceProvider CreateProvider(TestAi ai, bool failAfterSave = false, ReadGate? gate = null, TestClock? clock = null)
    {
        var services = new ServiceCollection();
        services.AddApplication();
        services.AddSingleton<IAiDialogClient>(ai);
        services.AddSingleton<TimeProvider>(clock ?? new TestClock());
        services.AddSingleton<IOptions<InstagramOptions>>(Options.Create(new InstagramOptions { AccountId = AccountId }));
        services.AddSingleton<IOptions<InboxProcessingOptions>>(Options.Create(new InboxProcessingOptions()));
        services.AddSingleton<IOptions<DialogOptions>>(Options.Create(new DialogOptions
        {
            PriceResponse = "Ответ о цене.", ServicesResponse = "Ответ об услугах.",
            FallbackResponse = "Уточнение недоступно.", HandoffOffer = "Передать менеджеру?",
            HandoffConfirmationPrompt = "Передать {need}?", AmbiguousConfirmationResponse = "Да или нет?"
        }));
        services.AddSingleton<IDialogTemplateProvider, ConfiguredDialogTemplateProvider>();
        services.AddSingleton<InstagramEventNormalizer>();
        services.AddDbContext<BurrfishDbContext>(options => options.UseNpgsql(fixture.ConnectionString));
        services.AddScoped<IConversationRepository>(p =>
        {
            var repository = new ConversationRepository(p.GetRequiredService<BurrfishDbContext>());
            return gate is null ? repository : new GatedRepository(repository, gate);
        });
        services.AddScoped<ILeadRepository, LeadRepository>();
        services.AddScoped<IOutboxWriter, OutboxWriter>();
        services.AddScoped<IConversationPersistence>(p =>
        {
            var persistence = new ConversationPersistence(p.GetRequiredService<BurrfishDbContext>());
            return failAfterSave ? new RetryAfterSave(persistence) : persistence;
        });
        services.AddScoped<InboxProcessor>();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    private static async Task ProcessAsync(ServiceProvider provider)
    {
        await using var scope = provider.CreateAsyncScope();
        Assert.True(await scope.ServiceProvider.GetRequiredService<InboxProcessor>().ProcessNextAsync(CancellationToken.None));
    }

    private async Task ClearQueueAsync()
    {
        await using var db = fixture.CreateContext();
        await db.OutboxMessages.ExecuteDeleteAsync();
        await db.InboxEvents.ExecuteDeleteAsync();
    }

    private async Task<string> SeedAwaitingAsync()
    {
        var sender = Guid.NewGuid().ToString("N");
        var model = Conversation.Start();
        model.ProposeHandoff("Нужен проект");
        await using var db = fixture.CreateContext();
        new ConversationRepository(db).Add(new StoredConversation(
            Guid.NewGuid(), $"{AccountId}:{sender}", sender, null, Now, model));
        await db.SaveChangesAsync();
        return sender;
    }

    private async Task<string> EnqueueAsync(string sender, string text)
    {
        var eventId = Guid.NewGuid().ToString("N");
        var payload = JsonSerializer.Serialize(new
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
                            sender = new { id = sender }, recipient = new { id = AccountId },
                            timestamp = Now.ToUnixTimeMilliseconds(), message = new { mid = eventId, text }
                        }
                    }
                }
            }
        });
        await using var db = fixture.CreateContext();
        Assert.True(await new InboxWriter(db).TryStoreAsync(new(eventId, payload, Now), CancellationToken.None));
        return eventId;
    }

    private async Task<bool> EnqueueDuplicateAsync(string eventId)
    {
        await using var db = fixture.CreateContext();
        return await new InboxWriter(db).TryStoreAsync(new(eventId, "{}", Now), CancellationToken.None);
    }

    private sealed class TestAi(
        bool fail = false,
        AiDialogDecisionKind decisionKind = AiDialogDecisionKind.Price) : IAiDialogClient
    {
        public int Calls { get; private set; }

        public Task<AiDialogResult> DecideAsync(AiDialogRequest request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(fail
                ? AiDialogResult.Failure(new("Timeout", "OpenAI request timed out.", null, true))
                : AiDialogResult.Success(new(
                    decisionKind,
                    decisionKind == AiDialogDecisionKind.AskClarifyingQuestion
                        ? "Уточните новый запрос."
                        : null)));
        }
    }

    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = Now;
        public override DateTimeOffset GetUtcNow() => UtcNow;
    }

    private sealed class RetryAfterSave(ConversationPersistence persistence) : IConversationPersistence
    {
        public async Task<PersistenceCommitResult> CommitAsync(CancellationToken cancellationToken)
        {
            Assert.True((await persistence.CommitAsync(cancellationToken)).Succeeded);
            return new(false, "InjectedRetry");
        }
    }

    private sealed class ReadGate
    {
        private int reads;
        private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task WaitAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref reads) >= 2)
            {
                ready.TrySetResult();
            }
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
        }
    }

    private sealed class GatedRepository(ConversationRepository repository, ReadGate gate) : IConversationRepository
    {
        public async Task<StoredConversation?> FindAsync(string externalConversationId, CancellationToken cancellationToken)
        {
            var stored = await repository.FindAsync(externalConversationId, cancellationToken);
            await gate.WaitAsync(cancellationToken);
            return stored;
        }

        public void Add(StoredConversation conversation) => repository.Add(conversation);
    }
}
