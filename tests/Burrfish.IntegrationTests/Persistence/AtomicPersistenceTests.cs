using System.Text.Json;
using Burrfish.Application.Persistence;
using Burrfish.Domain.Conversations;
using Burrfish.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Burrfish.IntegrationTests.Persistence;

[Collection(PostgresCollection.Name)]
public sealed class AtomicPersistenceTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Commit_persists_handoff_lead_and_outbox_together()
    {
        var externalId = Guid.NewGuid().ToString("N");
        var conversationId = Guid.NewGuid();
        await SeedAwaitingConversationAsync(conversationId, externalId);

        Guid leadId;
        await using (var db = fixture.CreateContext())
        {
            var stored = await new ConversationRepository(db).FindAsync(
                externalId,
                CancellationToken.None);
            Assert.NotNull(stored);

            var confirmation = stored.Conversation.ConfirmHandoff(
                new DateTimeOffset(2026, 10, 16, 12, 1, 0, TimeSpan.Zero));
            var draft = Assert.IsType<LeadDraft>(confirmation.Lead);

            leadId = new LeadRepository(db).Add(stored, draft);
            new OutboxWriter(db).Add(CreateOutbox(leadId));

            var result = await new ConversationPersistence(db).CommitAsync(
                CancellationToken.None);

            Assert.True(result.Succeeded);
            Assert.Null(result.ErrorCode);
        }

        await using var verificationDb = fixture.CreateContext();
        var persisted = await new ConversationRepository(verificationDb).FindAsync(
            externalId,
            CancellationToken.None);

        Assert.NotNull(persisted);
        Assert.Equal(ConversationState.OperatorHandoff, persisted.Conversation.State);
        Assert.Equal(1, await verificationDb.Leads.CountAsync(entity => entity.Id == leadId));
        Assert.Equal(
            1,
            await verificationDb.OutboxMessages.CountAsync(
                entity => entity.DeduplicationKey == $"telegram.lead-created.v1:{leadId:D}"));
    }

    [Fact]
    public async Task Rollback_leaves_no_partial_handoff_data()
    {
        var externalId = Guid.NewGuid().ToString("N");
        var conversationId = Guid.NewGuid();
        await SeedAwaitingConversationAsync(conversationId, externalId);

        Guid leadId;
        await using (var db = fixture.CreateContext())
        await using (var transaction = await db.Database.BeginTransactionAsync())
        {
            var stored = await new ConversationRepository(db).FindAsync(
                externalId,
                CancellationToken.None);
            Assert.NotNull(stored);

            var confirmation = stored.Conversation.ConfirmHandoff(
                new DateTimeOffset(2026, 10, 16, 12, 1, 0, TimeSpan.Zero));
            var draft = Assert.IsType<LeadDraft>(confirmation.Lead);

            leadId = new LeadRepository(db).Add(stored, draft);
            new OutboxWriter(db).Add(CreateOutbox(leadId));

            var result = await new ConversationPersistence(db).CommitAsync(
                CancellationToken.None);
            Assert.True(result.Succeeded);

            await transaction.RollbackAsync();
        }

        await using var verificationDb = fixture.CreateContext();
        var persisted = await new ConversationRepository(verificationDb).FindAsync(
            externalId,
            CancellationToken.None);

        Assert.NotNull(persisted);
        Assert.Equal(
            ConversationState.AwaitingHandoffConfirmation,
            persisted.Conversation.State);
        Assert.Equal(0, await verificationDb.Leads.CountAsync(entity => entity.Id == leadId));
        Assert.Equal(
            0,
            await verificationDb.OutboxMessages.CountAsync(
                entity => entity.DeduplicationKey == $"telegram.lead-created.v1:{leadId:D}"));
    }

    private async Task SeedAwaitingConversationAsync(Guid id, string externalId)
    {
        var model = Conversation.Start();
        model.ProposeHandoff("Нужна консультация");

        await using var db = fixture.CreateContext();
        new ConversationRepository(db).Add(new StoredConversation(
            id,
            externalId,
            "sender-003",
            "client",
            new DateTimeOffset(2026, 10, 16, 12, 0, 0, TimeSpan.Zero),
            model));
        await db.SaveChangesAsync();
    }

    private static OutboxEnvelope CreateOutbox(Guid leadId) => new(
        Guid.NewGuid(),
        $"telegram.lead-created.v1:{leadId:D}",
        "telegram.lead-created.v1",
        JsonSerializer.Serialize(new { LeadId = leadId }),
        new DateTimeOffset(2026, 10, 16, 12, 1, 0, TimeSpan.Zero));
}
