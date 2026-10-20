using System.Net;

namespace Burrfish.ApiContract.Tests.Instagram;

public sealed class InstagramWebhookVerificationTests
{
    [Fact]
    public async Task Valid_token_returns_plain_challenge()
    {
        using var factory = new InstagramWebhookFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(
            "/webhooks/instagram?hub.mode=subscribe"
            + "&hub.verify_token=test-verify-token"
            + "&hub.challenge=123456789");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("123456789", await response.Content.ReadAsStringAsync());
        Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
    }

    [Theory]
    [InlineData("?hub.mode=subscribe&hub.verify_token=wrong&hub.challenge=1")]
    [InlineData("?hub.mode=wrong&hub.verify_token=test-verify-token&hub.challenge=1")]
    [InlineData("?hub.mode=subscribe&hub.challenge=1")]
    [InlineData("?hub.mode=subscribe&hub.verify_token=test-verify-token")]
    [InlineData("?hub.mode=subscribe&hub.verify_token=test-verify-token&hub.verify_token=second&hub.challenge=1")]
    public async Task Invalid_verification_request_is_forbidden(string query)
    {
        using var factory = new InstagramWebhookFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync($"/webhooks/instagram{query}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
