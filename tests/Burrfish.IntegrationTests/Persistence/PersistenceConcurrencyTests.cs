using System.Text.Json;
using Burrfish.Application.Persistence;
using Burrfish.Domain.Conversations;
using Burrfish.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Burrfish.IntegrationTests.Persistence;

[Collection(PostgresCollection.Name)]
public sealed class PersistenceConcurrencyTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Concurrent_confirmation_has_one_winner()
    {
        var externalId = Guid.NewGuid().ToString("N");
        var conversationId = Guid.NewGuid();
        var model = Conversation.Start();
        model.ProposeHandoff("Нужна консультация");

        await using (var seedDb = fixture.CreateContext())
        {
            new ConversationRepository(seedDb).Add(new StoredConversation(
                conversationId,
                externalId,
                "sender-004",
                null,
                new DateTimeOffset(2026, 10, 16, 12, 0, 0, TimeSpan.Zero),
                model));
            await seedDb.SaveChangesAsync();
        }

        await using var firstDb = fixture.CreateContext();
        await using var secondDb = fixture.CreateContext();
        var first = await new ConversationRepository(firstDb).FindAsync(
            externalId,
            CancellationToken.None);
        var second = await new ConversationRepository(secondDb).FindAsync(
            externalId,
            CancellationToken.None);
        Assert.NotNull(first);
        Assert.NotNull(second);

        var firstLeadId = PrepareHandoff(firstDb, first);
        var secondLeadId = PrepareHandoff(secondDb, second);

        var firstResult = await new ConversationPersistence(firstDb).CommitAsync(
            CancellationToken.None);
        var secondResult = await new ConversationPersistence(secondDb).CommitAsync(
            CancellationToken.None);

        Assert.True(firstResult.Succeeded);
        Assert.False(secondResult.Succeeded);
        Assert.Equal("PersistenceConflict", secondResult.ErrorCode);

        await using var verificationDb = fixture.CreateContext();
        Assert.Equal(
            1,
            await verificationDb.Leads.CountAsync(
                entity => entity.HandoffKey == conversationId));
        Assert.Equal(
            1,
            await verificationDb.OutboxMessages.CountAsync(
                entity => entity.DeduplicationKey == $"telegram.lead-created.v1:{firstLeadId:D}"));
        Assert.Equal(
            0,
            await verificationDb.OutboxMessages.CountAsync(
                entity => entity.DeduplicationKey == $"telegram.lead-created.v1:{secondLeadId:D}"));
    }

    [Fact]
    public async Task Stale_conversation_update_returns_concurrency_conflict()
    {
        var externalId = Guid.NewGuid().ToString("N");
        var model = Conversation.Start();
        model.ProposeHandoff("Нужна консультация");

        await using (var seedDb = fixture.CreateContext())
        {
            new ConversationRepository(seedDb).Add(new StoredConversation(
                Guid.NewGuid(),
                externalId,
                "sender-005",
                null,
                new DateTimeOffset(2026, 10, 16, 12, 0, 0, TimeSpan.Zero),
                model));
            await seedDb.SaveChangesAsync();
        }

        await using var firstDb = fixture.CreateContext();
        await using var secondDb = fixture.CreateContext();
        var first = await new ConversationRepository(firstDb).FindAsync(
            externalId,
            CancellationToken.None);
        var second = await new ConversationRepository(secondDb).FindAsync(
            externalId,
            CancellationToken.None);
        Assert.NotNull(first);
        Assert.NotNull(second);

        Assert.True(first.Conversation.DeclineHandoff().Succeeded);
        Assert.True(second.Conversation.DeclineHandoff().Succeeded);

        var firstResult = await new ConversationPersistence(firstDb).CommitAsync(
            CancellationToken.None);
        var secondResult = await new ConversationPersistence(secondDb).CommitAsync(
            CancellationToken.None);

        Assert.True(firstResult.Succeeded);
        Assert.False(secondResult.Succeeded);
        Assert.Equal("ConcurrencyConflict", secondResult.ErrorCode);
    }

    private static Guid PrepareHandoff(BurrfishDbContext db, StoredConversation stored)
    {
        var confirmedAt = new DateTimeOffset(2026, 10, 16, 12, 1, 0, TimeSpan.Zero);
        var confirmation = stored.Conversation.ConfirmHandoff(confirmedAt);
        var draft = Assert.IsType<LeadDraft>(confirmation.Lead);
        var leadId = new LeadRepository(db).Add(stored, draft);
        new OutboxWriter(db).Add(new OutboxEnvelope(
            Guid.NewGuid(),
            $"telegram.lead-created.v1:{leadId:D}",
            "telegram.lead-created.v1",
            JsonSerializer.Serialize(new { LeadId = leadId }),
            confirmedAt));

        return leadId;
    }
}
