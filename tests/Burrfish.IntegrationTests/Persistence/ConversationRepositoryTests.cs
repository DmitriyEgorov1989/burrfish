using Burrfish.Application.Persistence;
using Burrfish.Domain.Conversations;
using Burrfish.Infrastructure.Persistence;

namespace Burrfish.IntegrationTests.Persistence;

[Collection(PostgresCollection.Name)]
public sealed class ConversationRepositoryTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Awaiting_conversation_roundtrip_preserves_pending_need()
    {
        const string need = "Нужна консультация по услугам";
        var externalId = Guid.NewGuid().ToString("N");
        var model = Conversation.Start();
        model.ProposeHandoff(need);
        var stored = new StoredConversation(
            Guid.NewGuid(),
            externalId,
            "sender-001",
            null,
            new DateTimeOffset(2026, 10, 16, 12, 0, 0, TimeSpan.Zero),
            model);

        await using (var writeDb = fixture.CreateContext())
        {
            new ConversationRepository(writeDb).Add(stored);
            await writeDb.SaveChangesAsync();
        }

        await using var readDb = fixture.CreateContext();
        var loaded = await new ConversationRepository(readDb).FindAsync(
            externalId,
            CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.Equal(ConversationState.AwaitingHandoffConfirmation, loaded.Conversation.State);
        Assert.Equal(need, loaded.Conversation.PendingNeed);

        var confirmation = loaded.Conversation.ConfirmHandoff(
            new DateTimeOffset(2026, 10, 16, 12, 1, 0, TimeSpan.Zero));

        Assert.True(confirmation.Succeeded);
        Assert.NotNull(confirmation.Lead);
    }

    [Fact]
    public async Task Tracked_confirmation_persists_operator_handoff()
    {
        var externalId = Guid.NewGuid().ToString("N");
        var model = Conversation.Start();
        model.ProposeHandoff("Нужна консультация");

        await using (var seedDb = fixture.CreateContext())
        {
            new ConversationRepository(seedDb).Add(new StoredConversation(
                Guid.NewGuid(),
                externalId,
                "sender-002",
                "client",
                new DateTimeOffset(2026, 10, 16, 12, 0, 0, TimeSpan.Zero),
                model));
            await seedDb.SaveChangesAsync();
        }

        await using (var updateDb = fixture.CreateContext())
        {
            var loaded = await new ConversationRepository(updateDb).FindAsync(
                externalId,
                CancellationToken.None);

            Assert.NotNull(loaded);
            var confirmation = loaded.Conversation.ConfirmHandoff(
                new DateTimeOffset(2026, 10, 16, 12, 1, 0, TimeSpan.Zero));
            Assert.True(confirmation.Succeeded);

            await updateDb.SaveChangesAsync();
        }

        await using var verificationDb = fixture.CreateContext();
        var persisted = await new ConversationRepository(verificationDb).FindAsync(
            externalId,
            CancellationToken.None);

        Assert.NotNull(persisted);
        Assert.Equal(ConversationState.OperatorHandoff, persisted.Conversation.State);
        Assert.Null(persisted.Conversation.PendingNeed);
        Assert.False(persisted.Conversation.CanReply);
    }
}
