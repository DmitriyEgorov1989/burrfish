using System.Net;
using System.Text;

namespace Burrfish.ApiContract.Tests.Instagram;

public sealed class InstagramWebhookSignatureTests
{
    [Fact]
    public async Task Missing_signature_is_unauthorized_without_write()
    {
        var writer = new RecordingInboxWriter();
        using var factory = new InstagramWebhookFactory(
            services => InstagramWebhookFactory.ReplaceInboxWriter(services, writer));
        using var client = factory.CreateClient();

        using var response = await client.PostAsync(
            "/webhooks/instagram",
            new StringContent("{", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(writer.Envelopes);
    }

    [Fact]
    public async Task Valid_signature_is_checked_before_json_is_parsed()
    {
        var writer = new RecordingInboxWriter();
        using var factory = new InstagramWebhookFactory(
            services => InstagramWebhookFactory.ReplaceInboxWriter(services, writer));
        using var client = factory.CreateClient();
        var body = "{"u8.ToArray();

        using var request = factory.CreateSignedPost(body);
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(writer.Envelopes);
    }

    [Fact]
    public async Task Signature_for_different_body_is_unauthorized()
    {
        var writer = new RecordingInboxWriter();
        using var factory = new InstagramWebhookFactory(
            services => InstagramWebhookFactory.ReplaceInboxWriter(services, writer));
        using var client = factory.CreateClient();
        using var request = factory.CreateSignedPost("{}"u8.ToArray());
        request.Content = new ByteArrayContent("{\"changed\":true}"u8.ToArray());

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(writer.Envelopes);
    }
}
