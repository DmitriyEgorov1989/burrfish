using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Burrfish.Infrastructure.Persistence;

/// <summary>Создаёт контекст только для design-time команд EF Core.</summary>
public sealed class BurrfishDbContextFactory : IDesignTimeDbContextFactory<BurrfishDbContext>
{
    /// <inheritdoc />
    public BurrfishDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<BurrfishDbContext>()
            .UseNpgsql()
            .Options;

        return new BurrfishDbContext(options);
    }
}
