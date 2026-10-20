using Burrfish.Infrastructure.Configuration;
using Burrfish.Infrastructure.Processing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Burrfish.Infrastructure.Tests.Processing;

public sealed class InboxWorkerTests
{
    [Fact]
    public async Task Disabled_worker_does_not_require_message_handler()
    {
        using var provider = new ServiceCollection().BuildServiceProvider();
        using var worker = CreateWorker(provider, enabled: false);

        await worker.StartAsync(CancellationToken.None);
        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Enabled_worker_rejects_start_without_message_handler()
    {
        using var provider = new ServiceCollection().BuildServiceProvider();
        using var worker = CreateWorker(provider, enabled: true);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => worker.StartAsync(CancellationToken.None));

        Assert.Contains(
            "ProcessIncomingMessageCommand handler",
            exception.Message,
            StringComparison.Ordinal);
    }

    private static InboxWorker CreateWorker(
        ServiceProvider provider,
        bool enabled) => new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new InboxProcessingOptions { Enabled = enabled }),
            TimeProvider.System,
            NullLogger<InboxWorker>.Instance);
}
