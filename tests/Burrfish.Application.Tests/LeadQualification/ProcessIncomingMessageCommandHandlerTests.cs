using System.Text.Json;
using Burrfish.Application.Instagram;
using Burrfish.Application.LeadQualification;
using Burrfish.Application.OpenAI;
using Burrfish.Application.Persistence;
using Burrfish.Domain.Conversations;

namespace Burrfish.Application.Tests.LeadQualification;

public sealed class ProcessIncomingMessageCommandHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(AiDialogDecisionKind.Price, "Утверждённая цена.")]
    [InlineData(AiDialogDecisionKind.Services, "Утверждённые услуги.")]
    public async Task Known_intent_uses_template_and_waits_for_confirmation(
        AiDialogDecisionKind kind, string expectedResponse)
    {
        var ports = new TestPorts { AiResult = AiDialogResult.Success(new(kind, null)) };

        var result = await CreateHandler(ports).Handle(Command(), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(ConversationState.AwaitingHandoffConfirmation, ports.Stored!.Conversation.State);
        Assert.Equal("Сколько стоит?", ports.Stored.Conversation.PendingNeed);
        var job = Assert.Single(ports.Outbox);
        Assert.Equal("instagram.reply.v1:event-1", job.DeduplicationKey);
        using var payload = JsonDocument.Parse(job.Payload);
        Assert.Equal(expectedResponse + "\n\nПередать менеджеру?",
            payload.RootElement.GetProperty("text").GetString());
        Assert.Equal("sender-1", payload.RootElement.GetProperty("recipientId").GetString());
        Assert.Empty(ports.Leads);
        Assert.Equal(1, ports.Commits);
        Assert.Empty(ports.LastAiRequest!.History);
    }

    [Theory]
    [InlineData(AiDialogDecisionKind.AskClarifyingQuestion, "Какой результат нужен?", "Какой результат нужен?", ConversationState.BotActive)]
    [InlineData(AiDialogDecisionKind.ProposeHandoff, "Нужен проект", "Передать запрос: Нужен проект?", ConversationState.AwaitingHandoffConfirmation)]
    public async Task Unknown_intent_clarifies_or_proposes_need(
        AiDialogDecisionKind kind, string text, string expected, ConversationState state)
    {
        var ports = new TestPorts { AiResult = AiDialogResult.Success(new(kind, text)) };

        var result = await CreateHandler(ports).Handle(Command(), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(state, ports.Stored!.Conversation.State);
        Assert.Equal(state == ConversationState.BotActive ? null : text, ports.Stored.Conversation.PendingNeed);
        Assert.Equal(expected, ReplyText(Assert.Single(ports.Outbox)));
        Assert.Empty(ports.Leads);
    }

    [Fact]
    public async Task Ai_failure_offers_handoff_of_original_question_without_exposing_error()
    {
        var error = new AiDialogError("rate_limit_exceeded", "private-error-marker", 429, true);
        var ports = new TestPorts { AiResult = AiDialogResult.Failure(error) };

        var result = await CreateHandler(ports).Handle(Command(), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(ConversationState.AwaitingHandoffConfirmation, ports.Stored!.Conversation.State);
        Assert.Equal("Сколько стоит?", ports.Stored.Conversation.PendingNeed);
        Assert.Equal("Уточнение недоступно.\n\nПередать менеджеру?", ReplyText(Assert.Single(ports.Outbox)));
        Assert.Empty(ports.Leads);
    }

    [Fact]
    public async Task Confirmation_creates_lead_and_notification_then_silences_bot()
    {
        var ports = AwaitingPorts();
        var handler = CreateHandler(ports);

        Assert.True((await handler.Handle(Command("Да"), CancellationToken.None)).Succeeded);

        Assert.Equal(ConversationState.OperatorHandoff, ports.Stored!.Conversation.State);
        Assert.Null(ports.Stored.Conversation.PendingNeed);
        var draft = Assert.Single(ports.Leads);
        Assert.Equal("Нужен проект", draft.Need);
        Assert.Equal(Now, draft.CreatedAt);
        var notification = Assert.Single(ports.Outbox);
        Assert.Equal("telegram.lead-created.v1", notification.Type);
        Assert.Equal($"telegram.lead-created.v1:{ports.LeadId:D}", notification.DeduplicationKey);
        using var payload = JsonDocument.Parse(notification.Payload);
        Assert.Equal(ports.LeadId, payload.RootElement.GetProperty("LeadId").GetGuid());

        Assert.True((await handler.Handle(Command("Ещё вопрос"), CancellationToken.None)).Succeeded);
        Assert.Single(ports.Leads);
        Assert.Single(ports.Outbox);
        Assert.Equal(0, ports.AiCalls);
        Assert.Equal(1, ports.Commits);
    }

    [Theory]
    [InlineData("Нет", ConversationState.BotActive, null)]
    [InlineData("Наверное", ConversationState.AwaitingHandoffConfirmation, "Ответьте да или нет.")]
    [InlineData("да, но пока не передавайте", ConversationState.AwaitingHandoffConfirmation, "Ответьте да или нет.")]
    public async Task Non_confirmation_never_creates_lead(
        string text, ConversationState state, string? expectedReply)
    {
        var ports = AwaitingPorts();

        var result = await CreateHandler(ports).Handle(Command(text), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(state, ports.Stored!.Conversation.State);
        Assert.Equal(state == ConversationState.BotActive ? null : "Нужен проект", ports.Stored.Conversation.PendingNeed);
        if (expectedReply is null)
        {
            Assert.Empty(ports.Outbox);
        }
        else
        {
            Assert.Equal(expectedReply, ReplyText(Assert.Single(ports.Outbox)));
        }
        Assert.Empty(ports.Leads);
        Assert.Equal(0, ports.AiCalls);
    }

    [Fact]
    public async Task Yes_outside_confirmation_state_does_not_create_lead()
    {
        var ports = new TestPorts
        {
            AiResult = AiDialogResult.Success(new(AiDialogDecisionKind.AskClarifyingQuestion, "Что вам нужно?"))
        };

        Assert.True((await CreateHandler(ports).Handle(Command("Да"), CancellationToken.None)).Succeeded);

        Assert.Equal(ConversationState.BotActive, ports.Stored!.Conversation.State);
        Assert.Empty(ports.Leads);
        Assert.Equal(1, ports.AiCalls);
    }

    [Fact]
    public async Task Persistence_conflict_is_returned_for_inbox_retry()
    {
        var ports = new TestPorts { CommitResult = new(false, "ConcurrencyConflict") };

        var result = await CreateHandler(ports).Handle(Command(), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("ConcurrencyConflict", result.ErrorCode);
    }

    [Fact]
    public async Task Caller_cancellation_does_not_schedule_fallback()
    {
        var ports = new TestPorts { CancelAi = true };
        using var source = new CancellationTokenSource();
        source.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CreateHandler(ports).Handle(Command(), source.Token));

        Assert.Empty(ports.Outbox);
        Assert.Empty(ports.Leads);
        Assert.Equal(0, ports.Commits);
    }

    [Fact]
    public async Task Reply_payload_preserves_quotes_newlines_and_cyrillic()
    {
        const string text = "Уточните \"задачу\".\nКакие пожелания?";
        var ports = new TestPorts { AiResult = AiDialogResult.Success(new(AiDialogDecisionKind.AskClarifyingQuestion, text)) };

        await CreateHandler(ports).Handle(Command(), CancellationToken.None);

        Assert.Equal(text, ReplyText(Assert.Single(ports.Outbox)));
    }

    private static TestPorts AwaitingPorts()
    {
        var conversation = Conversation.Start();
        conversation.ProposeHandoff("Нужен проект");
        return new TestPorts
        {
            Stored = new StoredConversation(Guid.NewGuid(), "conversation-1", "sender-1", null, Now, conversation)
        };
    }

    private static string? ReplyText(OutboxEnvelope job)
    {
        using var payload = JsonDocument.Parse(job.Payload);
        return payload.RootElement.GetProperty("text").GetString();
    }

    private static ProcessIncomingMessageCommand Command(string text = "Сколько стоит?") => new(
        new IncomingInstagramMessage("event-1", "conversation-1", "sender-1", null, text, Now));

    private static ProcessIncomingMessageCommandHandler CreateHandler(TestPorts ports) => new(
        ports, ports, ports, ports, ports, ports, new FixedTimeProvider());

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class TestPorts : IConversationRepository, IAiDialogClient,
        IDialogTemplateProvider, ILeadRepository, IOutboxWriter, IConversationPersistence
    {
        public StoredConversation? Stored { get; set; }
        public AiDialogResult AiResult { get; set; } =
            AiDialogResult.Success(new(AiDialogDecisionKind.Price, null));
        public AiDialogRequest? LastAiRequest { get; private set; }
        public int AiCalls { get; private set; }
        public int Commits { get; private set; }
        public List<OutboxEnvelope> Outbox { get; } = [];
        public List<LeadDraft> Leads { get; } = [];
        public Guid LeadId { get; } = Guid.NewGuid();
        public PersistenceCommitResult CommitResult { get; set; } = new(true, null);
        public bool CancelAi { get; set; }

        public Task<StoredConversation?> FindAsync(string externalConversationId, CancellationToken cancellationToken)
            => Task.FromResult(Stored);

        public void Add(StoredConversation conversation) => Stored = conversation;

        public Task<AiDialogResult> DecideAsync(AiDialogRequest request, CancellationToken cancellationToken)
        {
            AiCalls++;
            LastAiRequest = request;
            if (CancelAi)
            {
                return Task.FromCanceled<AiDialogResult>(cancellationToken);
            }

            return Task.FromResult(AiResult);
        }

        public DialogTemplates Get() => new(
            "Утверждённая цена.", "Утверждённые услуги.", "Уточнение недоступно.",
            "Передать менеджеру?", "Передать запрос: {need}?", "Ответьте да или нет.");

        public Guid Add(StoredConversation conversation, LeadDraft draft)
        {
            Assert.Equal(ConversationState.OperatorHandoff, conversation.Conversation.State);
            Leads.Add(draft);
            return LeadId;
        }

        public void Add(OutboxEnvelope envelope) => Outbox.Add(envelope);

        public Task<PersistenceCommitResult> CommitAsync(CancellationToken cancellationToken)
        {
            Commits++;
            return Task.FromResult(CommitResult);
        }
    }
}
