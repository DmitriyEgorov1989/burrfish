using Microsoft.EntityFrameworkCore;

namespace Burrfish.IntegrationTests.Persistence;

[Collection(PostgresCollection.Name)]
public sealed class MigrationTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Initial_migration_creates_required_tables()
    {
        await using var db = fixture.CreateContext();

        var tables = await db.Database
            .SqlQueryRaw<string>(
                """
                SELECT table_name AS "Value"
                FROM information_schema.tables
                WHERE table_schema = 'public'
                  AND table_name IN ('inbox_events', 'outbox_messages', 'conversations', 'leads')
                ORDER BY table_name
                """)
            .ToArrayAsync();

        Assert.Equal(
            ["conversations", "inbox_events", "leads", "outbox_messages"],
            tables);

        var indexes = await db.Database
            .SqlQueryRaw<string>(
                """
                SELECT indexname AS "Value"
                FROM pg_indexes
                WHERE schemaname = 'public'
                  AND indexname IN (
                      'ix_inbox_pending',
                      'ix_outbox_pending',
                      'ux_conversations_external_id',
                      'ux_inbox_external_event_id',
                      'ux_leads_handoff_key',
                      'ux_outbox_deduplication_key')
                ORDER BY indexname
                """)
            .ToArrayAsync();

        Assert.Equal(
            [
                "ix_inbox_pending",
                "ix_outbox_pending",
                "ux_conversations_external_id",
                "ux_inbox_external_event_id",
                "ux_leads_handoff_key",
                "ux_outbox_deduplication_key"
            ],
            indexes);
    }
}
