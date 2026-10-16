using System.Security.Cryptography;
using Burrfish.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Burrfish.IntegrationTests.Persistence;

[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "PostgreSQL persistence";
}

public sealed class PostgresFixture : IAsyncLifetime
{
    private const string Image =
        "postgres:17-bookworm@sha256:3645570cccdfa447589da9f57dd740faa29b30938e861289a5574b6ca6b03826";

    private readonly PostgreSqlContainer container = new PostgreSqlBuilder(Image)
        .WithPassword(Convert.ToHexString(RandomNumberGenerator.GetBytes(32)))
        .Build();

    public BurrfishDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<BurrfishDbContext>()
            .UseNpgsql(container.GetConnectionString())
            .EnableSensitiveDataLogging(false)
            .Options;

        return new BurrfishDbContext(options);
    }

    public async Task InitializeAsync()
    {
        await container.StartAsync();

        await using var db = CreateContext();
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => container.DisposeAsync().AsTask();
}
