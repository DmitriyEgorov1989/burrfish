using System.Net;
using Burrfish.ApiContract.Tests.Instagram;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Burrfish.ApiContract.Tests.Health;

public sealed class HealthEndpointTests
{
    [Fact]
    public async Task Live_returns_ok_without_running_readiness_checks()
    {
        using var factory = CreateFactory(
            HealthCheckResult.Unhealthy("database-marker"),
            HealthCheckResult.Unhealthy("worker-marker"));
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Ready_returns_ok_when_database_and_workers_are_healthy()
    {
        using var factory = CreateFactory(
            HealthCheckResult.Healthy(),
            HealthCheckResult.Healthy());
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task Ready_returns_service_unavailable_when_a_required_check_fails(
        bool databaseHealthy,
        bool workersHealthy)
    {
        const string sensitiveMarker = "sensitive-health-detail-marker";
        using var factory = CreateFactory(
            databaseHealthy
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy(sensitiveMarker, new InvalidOperationException(sensitiveMarker)),
            workersHealthy
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy(sensitiveMarker, new InvalidOperationException(sensitiveMarker)));
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/ready");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("Unhealthy", body);
        Assert.DoesNotContain(sensitiveMarker, body, StringComparison.Ordinal);
    }

    private static InstagramWebhookFactory CreateFactory(
        HealthCheckResult database,
        HealthCheckResult workers) => new(services =>
    {
        services.Configure<HealthCheckServiceOptions>(options =>
        {
            options.Registrations.Clear();
            options.Registrations.Add(CreateRegistration("postgresql", database, "ready"));
            options.Registrations.Add(CreateRegistration("workers", workers, "ready"));
            options.Registrations.Add(CreateRegistration(
                "external-api",
                HealthCheckResult.Unhealthy("must not affect readiness")));
        });
    });

    private static HealthCheckRegistration CreateRegistration(
        string name,
        HealthCheckResult result,
        params string[] tags) => new(
            name,
            _ => new StubHealthCheck(result),
            HealthStatus.Unhealthy,
            tags);

    private sealed class StubHealthCheck(HealthCheckResult result) : IHealthCheck
    {
        public Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context,
            CancellationToken cancellationToken = default) => Task.FromResult(result);
    }
}
