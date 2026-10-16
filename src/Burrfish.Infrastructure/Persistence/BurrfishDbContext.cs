using Burrfish.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Burrfish.Infrastructure.Persistence;

/// <summary>Контекст PostgreSQL для надёжной обработки сообщений и заявок.</summary>
public sealed class BurrfishDbContext(DbContextOptions<BurrfishDbContext> options) : DbContext(options)
{
    /// <summary>Входящие события.</summary>
    public DbSet<InboxEvent> InboxEvents => Set<InboxEvent>();

    /// <summary>Исходящие задания.</summary>
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    /// <summary>Состояния диалогов.</summary>
    public DbSet<ConversationEntity> Conversations => Set<ConversationEntity>();

    /// <summary>Подтверждённые заявки.</summary>
    public DbSet<LeadEntity> Leads => Set<LeadEntity>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(BurrfishDbContext).Assembly);
    }
}
