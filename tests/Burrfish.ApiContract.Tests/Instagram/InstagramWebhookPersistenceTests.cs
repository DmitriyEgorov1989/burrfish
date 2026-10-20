using System.Net;
using Burrfish.Application.Persistence;

namespace Burrfish.ApiContract.Tests.Instagram;

public sealed class InstagramWebhookPersistenceTests
{
    [Fact]
    public async Task Valid_event_is_stored_before_success_response()
    {
        var writer = new BlockingInboxWriter();
        using var factory = new InstagramWebhookFactory(
            services => InstagramWebhookFactory.ReplaceInboxWriter(services, writer));
        using var client = factory.CreateClient();
        var body = await FixtureBodyAsync();

        using var request = factory.CreateSignedPost(body);
        var responseTask = client.SendAsync(request);

        try
        {
            await writer.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(responseTask.IsCompleted);
        }
        finally
        {
            writer.Release.TrySetResult(true);
        }

        using var response = await responseTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Duplicate_is_acknowledged_as_success()
    {
        var writer = new RecordingInboxWriter(result: false);
        using var factory = new InstagramWebhookFactory(
            services => InstagramWebhookFactory.ReplaceInboxWriter(services, writer));
        using var client = factory.CreateClient();

        using var request = factory.CreateSignedPost(await FixtureBodyAsync());
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(writer.Envelopes);
    }

    [Fact]
    public async Task Body_larger_than_one_mebibyte_is_rejected_without_write()
    {
        var writer = new RecordingInboxWriter();
        using var factory = new InstagramWebhookFactory(
            services => InstagramWebhookFactory.ReplaceInboxWriter(services, writer));
        using var client = factory.CreateClient();
        var body = new byte[1_048_577];

        using var request = factory.CreateSignedPost(body);
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Empty(writer.Envelopes);
    }

    private static Task<byte[]> FixtureBodyAsync() => File.ReadAllBytesAsync(
        Path.Combine(AppContext.BaseDirectory, "Instagram", "Fixtures", "text-message.json"));

    private sealed class BlockingInboxWriter : IInboxWriter
    {
        public TaskCompletionSource<bool> Entered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<bool> Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<bool> TryStoreAsync(
            InboxEnvelope envelope,
            CancellationToken cancellationToken)
        {
            Entered.TrySetResult(true);
            await Release.Task.WaitAsync(cancellationToken);
            return true;
        }
    }
}
