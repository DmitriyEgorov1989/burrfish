using System.Text.Json;
using Burrfish.Application.Instagram;
using Burrfish.Application.OpenAI;
using Burrfish.Application.Persistence;
using Burrfish.Domain.Conversations;
using MediatR;

namespace Burrfish.Application.LeadQualification;

/// <summary>Выбирает ответ и сохраняет действия диалога в текущей единице сохранения.</summary>
public sealed class ProcessIncomingMessageCommandHandler(
    IConversationRepository conversations,
    IAiDialogClient ai,
    IDialogTemplateProvider templateProvider,
    ILeadRepository leads,
    IOutboxWriter outbox,
    IConversationPersistence persistence,
    TimeProvider timeProvider)
    : IRequestHandler<ProcessIncomingMessageCommand, ProcessIncomingMessageResult>
{
    /// <inheritdoc />
    public async Task<ProcessIncomingMessageResult> Handle(
        ProcessIncomingMessageCommand request,
        CancellationToken cancellationToken)
    {
        var message = request.Message;
        var stored = await conversations.FindAsync(message.ConversationId, cancellationToken);
        if (stored is null)
        {
            stored = new StoredConversation(
                Guid.NewGuid(), message.ConversationId, message.SenderId, message.Username,
                timeProvider.GetUtcNow(), Conversation.Start());
            conversations.Add(stored);
        }

        if (!stored.Conversation.CanReply)
        {
            return new(true, null);
        }

        var templates = templateProvider.Get();
        DialogReply reply;
        if (stored.Conversation.State == ConversationState.AwaitingHandoffConfirmation)
        {
            switch (ConfirmationClassifier.Classify(message.Text))
            {
                case ConfirmationKind.Confirmed:
                    var confirmation = stored.Conversation.ConfirmHandoff(timeProvider.GetUtcNow());
                    if (!confirmation.Succeeded)
                    {
                        return new(false, confirmation.ErrorCode);
                    }
                    reply = new(null, confirmation.Lead);
                    break;
                case ConfirmationKind.Declined:
                    var decline = stored.Conversation.DeclineHandoff();
                    if (!decline.Succeeded)
                    {
                        return new(false, decline.ErrorCode);
                    }
                    reply = new(null, null);
                    break;
                default:
                    reply = new(templates.AmbiguousConfirmationResponse, null);
                    break;
            }
        }
        else
        {
            var result = await ai.DecideAsync(new AiDialogRequest(message.Text, []), cancellationToken);
            var decision = result.Decision;
            string? need = null;
            string response;
            switch (decision?.Kind)
            {
                case AiDialogDecisionKind.Price:
                    response = $"{templates.PriceResponse}\n\n{templates.HandoffOffer}";
                    need = message.Text;
                    break;
                case AiDialogDecisionKind.Services:
                    response = $"{templates.ServicesResponse}\n\n{templates.HandoffOffer}";
                    need = message.Text;
                    break;
                case AiDialogDecisionKind.AskClarifyingQuestion:
                    response = decision.Text!;
                    break;
                case AiDialogDecisionKind.ProposeHandoff:
                    need = decision.Text!;
                    response = templates.HandoffConfirmationPrompt.Replace("{need}", need, StringComparison.Ordinal);
                    break;
                default:
                    response = $"{templates.FallbackResponse}\n\n{templates.HandoffOffer}";
                    need = message.Text;
                    break;
            }

            if (need is not null)
            {
                var transition = stored.Conversation.ProposeHandoff(need);
                if (!transition.Succeeded)
                {
                    return new(false, transition.ErrorCode);
                }
            }
            reply = new(response, null);
        }

        if (reply.Text is not null)
        {
            outbox.Add(new OutboxEnvelope(
                Guid.NewGuid(), $"instagram.reply.v1:{message.ExternalEventId}", "instagram.reply.v1",
                JsonSerializer.Serialize(new
                {
                    recipientId = message.SenderId,
                    text = reply.Text
                }), timeProvider.GetUtcNow()));
        }

        if (reply.Lead is not null)
        {
            var leadId = leads.Add(stored, reply.Lead);
            outbox.Add(new OutboxEnvelope(
                Guid.NewGuid(), $"telegram.lead-created.v1:{leadId:D}", "telegram.lead-created.v1",
                JsonSerializer.Serialize(new { LeadId = leadId }), timeProvider.GetUtcNow()));
        }

        var commit = await persistence.CommitAsync(cancellationToken);
        return new(commit.Succeeded, commit.ErrorCode);
    }
}
