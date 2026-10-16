using Burrfish.Domain.Conversations;

namespace Burrfish.Domain.Tests.Conversations;

public sealed class ConversationTests
{
    private const string Need = "Нужна консультация по услугам";
    private static readonly DateTimeOffset ConfirmedAt =
        new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Start_creates_active_conversation()
    {
        var conversation = Conversation.Start();

        Assert.Equal(ConversationState.BotActive, conversation.State);
        Assert.True(conversation.CanReply);
        Assert.Null(conversation.PendingNeed);
    }

    [Fact]
    public void Propose_handoff_stores_need_and_waits_for_confirmation()
    {
        var conversation = Conversation.Start();

        var result = conversation.ProposeHandoff($"  {Need}  ");

        Assert.True(result.Succeeded);
        Assert.Null(result.ErrorCode);
        Assert.Equal(ConversationState.AwaitingHandoffConfirmation, conversation.State);
        Assert.Equal(Need, conversation.PendingNeed);
        Assert.True(conversation.CanReply);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\r\n")]
    public void Empty_need_is_rejected_without_changing_state(string? need)
    {
        var conversation = Conversation.Start();

        var result = conversation.ProposeHandoff(need);

        Assert.False(result.Succeeded);
        Assert.Equal("NeedRequired", result.ErrorCode);
        Assert.Equal(ConversationState.BotActive, conversation.State);
        Assert.Null(conversation.PendingNeed);
        Assert.True(conversation.CanReply);
    }

    [Fact]
    public void Confirmation_creates_draft_and_disables_replies()
    {
        var conversation = Conversation.Start();
        conversation.ProposeHandoff(Need);

        var result = conversation.ConfirmHandoff(ConfirmedAt);

        Assert.True(result.Succeeded);
        Assert.Null(result.ErrorCode);
        var lead = Assert.IsType<LeadDraft>(result.Lead);
        Assert.Equal(Need, lead.Need);
        Assert.Equal(ConfirmedAt, lead.CreatedAt);
        Assert.Equal(ConversationState.OperatorHandoff, conversation.State);
        Assert.Null(conversation.PendingNeed);
        Assert.False(conversation.CanReply);
    }

    [Fact]
    public void Decline_clears_need_and_allows_another_proposal()
    {
        var conversation = Conversation.Start();
        conversation.ProposeHandoff(Need);

        var declined = conversation.DeclineHandoff();

        Assert.True(declined.Succeeded);
        Assert.Null(declined.ErrorCode);
        Assert.Equal(ConversationState.BotActive, conversation.State);
        Assert.Null(conversation.PendingNeed);
        Assert.True(conversation.CanReply);

        var proposed = conversation.ProposeHandoff("Нужна другая консультация");

        Assert.True(proposed.Succeeded);
        Assert.Equal("Нужна другая консультация", conversation.PendingNeed);
    }

    [Theory]
    [InlineData(ConversationState.BotActive, "Confirm")]
    [InlineData(ConversationState.BotActive, "Decline")]
    [InlineData(ConversationState.AwaitingHandoffConfirmation, "Propose")]
    [InlineData(ConversationState.OperatorHandoff, "Propose")]
    [InlineData(ConversationState.OperatorHandoff, "Confirm")]
    [InlineData(ConversationState.OperatorHandoff, "Decline")]
    public void Invalid_transition_is_rejected_without_changing_conversation(
        ConversationState state,
        string operation)
    {
        var conversation = CreateConversation(state);
        var previousNeed = conversation.PendingNeed;
        var previousCanReply = conversation.CanReply;

        ConversationTransitionResult result;

        switch (operation)
        {
            case "Propose":
                result = conversation.ProposeHandoff("Другая потребность");
                break;
            case "Confirm":
                var confirmation = conversation.ConfirmHandoff(ConfirmedAt);
                Assert.Null(confirmation.Lead);
                result = new(confirmation.Succeeded, confirmation.ErrorCode);
                break;
            case "Decline":
                result = conversation.DeclineHandoff();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(operation));
        }

        Assert.False(result.Succeeded);
        Assert.Equal("InvalidState", result.ErrorCode);
        Assert.Equal(state, conversation.State);
        Assert.Equal(previousNeed, conversation.PendingNeed);
        Assert.Equal(previousCanReply, conversation.CanReply);
    }

    [Fact]
    public void Confirmation_normalizes_creation_time_to_utc()
    {
        var conversation = Conversation.Start();
        conversation.ProposeHandoff(Need);
        var localTime = new DateTimeOffset(
            2026,
            10,
            16,
            15,
            0,
            0,
            TimeSpan.FromHours(3));

        var result = conversation.ConfirmHandoff(localTime);

        var lead = Assert.IsType<LeadDraft>(result.Lead);
        Assert.Equal(TimeSpan.Zero, lead.CreatedAt.Offset);
        Assert.Equal(ConfirmedAt, lead.CreatedAt);
    }

    private static Conversation CreateConversation(ConversationState state)
    {
        var conversation = Conversation.Start();

        if (state != ConversationState.BotActive)
        {
            conversation.ProposeHandoff(Need);
        }

        if (state == ConversationState.OperatorHandoff)
        {
            conversation.ConfirmHandoff(ConfirmedAt);
        }

        return conversation;
    }
}
