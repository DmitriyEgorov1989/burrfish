using Burrfish.Domain.Conversations;
using Burrfish.Infrastructure.Configuration;
using Burrfish.Infrastructure.Persistence;
using Burrfish.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Burrfish.IntegrationTests.Persistence;

[Collection(PostgresCollection.Name)]
public sealed class DataRetentionServiceTests(PostgresFixture fixture)
{
    private static readonly DateTimeOffset Now =
        new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task DeleteExpiredAsync_removes_records_at_cutoff_and_keeps_newer_records()
    {
        var cutoff = Now.AddDays(-30);
        var expiredConversationId = Guid.NewGuid();
        var freshConversationId = Guid.NewGuid();
        var expiredInboxId = Guid.NewGuid();
        var freshInboxId = Guid.NewGuid();
        var expiredOutboxId = Guid.NewGuid();
        var freshOutboxId = Guid.NewGuid();
        var expiredLeadId = Guid.NewGuid();
        var freshLeadId = Guid.NewGuid();

        await using (var db = fixture.CreateContext())
        {
            db.Conversations.AddRange(
                CreateConversation(expiredConversationId, cutoff),
                CreateConversation(freshConversationId, cutoff.AddSeconds(1)));
            db.InboxEvents.AddRange(
                CreateInbox(expiredInboxId, cutoff),
                CreateInbox(freshInboxId, cutoff.AddSeconds(1)));
            db.OutboxMessages.AddRange(
                CreateOutbox(expiredOutboxId, cutoff, OutboxMessageStatus.Pending),
                CreateOutbox(freshOutboxId, cutoff.AddSeconds(1), OutboxMessageStatus.Failed));
            db.Leads.AddRange(
                CreateLead(expiredLeadId, expiredConversationId, cutoff),
                CreateLead(freshLeadId, freshConversationId, cutoff.AddSeconds(1)));
            await db.SaveChangesAsync();
        }

        await using (var db = fixture.CreateContext())
        {
            var service = new DataRetentionService(
                db,
                new FixedTimeProvider(Now),
                Options.Create(new DialogOptions { RetentionDays = 30 }));

            var result = await service.DeleteExpiredAsync(CancellationToken.None);

            Assert.Equal(cutoff, result.Cutoff);
            Assert.Equal(1, result.DeletedInboxEvents);
            Assert.Equal(1, result.DeletedOutboxMessages);
            Assert.Equal(1, result.DeletedConversations);
            Assert.Equal(1, result.DeletedLeads);
            Assert.Equal(1, result.DeletedPendingOutboxMessages);
        }

        await using var verificationDb = fixture.CreateContext();
        Assert.False(await verificationDb.Conversations.AnyAsync(x => x.Id == expiredConversationId));
        Assert.False(await verificationDb.InboxEvents.AnyAsync(x => x.Id == expiredInboxId));
        Assert.False(await verificationDb.OutboxMessages.AnyAsync(x => x.Id == expiredOutboxId));
        Assert.False(await verificationDb.Leads.AnyAsync(x => x.Id == expiredLeadId));
        Assert.True(await verificationDb.Conversations.AnyAsync(x => x.Id == freshConversationId));
        Assert.True(await verificationDb.InboxEvents.AnyAsync(x => x.Id == freshInboxId));
        Assert.True(await verificationDb.OutboxMessages.AnyAsync(x => x.Id == freshOutboxId));
        Assert.True(await verificationDb.Leads.AnyAsync(x => x.Id == freshLeadId));
    }

    [Theory]
    [InlineData(InboxEventStatus.Pending, OutboxMessageStatus.Pending)]
    [InlineData(InboxEventStatus.Processing, OutboxMessageStatus.Processing)]
    [InlineData(InboxEventStatus.Failed, OutboxMessageStatus.Failed)]
    [InlineData(InboxEventStatus.Processed, OutboxMessageStatus.Sent)]
    public async Task DeleteExpiredAsync_removes_expired_queue_payloads_regardless_of_status(
        InboxEventStatus inboxStatus,
        OutboxMessageStatus outboxStatus)
    {
        var inboxId = Guid.NewGuid();
        var outboxId = Guid.NewGuid();
        var expiredAt = Now.AddDays(-30).AddSeconds(-1);

        await using (var db = fixture.CreateContext())
        {
            var inbox = CreateInbox(inboxId, expiredAt);
            inbox.Status = inboxStatus;
            var outbox = CreateOutbox(outboxId, expiredAt, outboxStatus);
            outbox.NextAttemptAt = Now.AddDays(10);
            db.AddRange(inbox, outbox);
            await db.SaveChangesAsync();
        }

        await using (var db = fixture.CreateContext())
        {
            var service = new DataRetentionService(
                db,
                new FixedTimeProvider(Now),
                Options.Create(new DialogOptions { RetentionDays = 30 }));
            await service.DeleteExpiredAsync(CancellationToken.None);
        }

        await using var verificationDb = fixture.CreateContext();
        Assert.False(await verificationDb.InboxEvents.AnyAsync(x => x.Id == inboxId));
        Assert.False(await verificationDb.OutboxMessages.AnyAsync(x => x.Id == outboxId));
    }

    private static ConversationEntity CreateConversation(Guid id, DateTimeOffset createdAt) => new()
    {
        Id = id,
        ExternalConversationId = $"conversation-{id:N}",
        SenderId = $"sender-{id:N}",
        CreatedAt = createdAt,
        Model = Conversation.Start()
    };

    private static InboxEvent CreateInbox(Guid id, DateTimeOffset receivedAt) => new()
    {
        Id = id,
        ExternalEventId = $"event-{id:N}",
        Payload = "{\"message\":\"sensitive-inbox-marker\"}",
        ReceivedAt = receivedAt,
        Status = InboxEventStatus.Pending,
        NextAttemptAt = receivedAt
    };

    private static OutboxMessage CreateOutbox(
        Guid id,
        DateTimeOffset createdAt,
        OutboxMessageStatus status) => new()
    {
        Id = id,
        DeduplicationKey = $"retention:{id:N}",
        Type = "instagram.reply.v1",
        Payload = "{\"text\":\"sensitive-outbox-marker\"}",
        CreatedAt = createdAt,
        Status = status,
        NextAttemptAt = createdAt
    };

    private static LeadEntity CreateLead(
        Guid id,
        Guid conversationId,
        DateTimeOffset createdAt) => new()
    {
        Id = id,
        ConversationId = conversationId,
        HandoffKey = Guid.NewGuid(),
        SenderId = $"sender-{id:N}",
        Need = "sensitive-lead-marker",
        CreatedAt = createdAt,
        NotificationStatus = LeadNotificationStatus.Pending
    };

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
