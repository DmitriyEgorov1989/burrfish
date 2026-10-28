using Burrfish.Infrastructure.Persistence;
using Burrfish.Infrastructure.Processing;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Burrfish.Api.Configuration;

/// <summary>Регистрирует безопасные проверки живости и готовности приложения.</summary>
public static class HealthRegistration
{
    private const string ReadyTag = "ready";

    /// <summary>Добавляет проверки PostgreSQL и фоновых обработчиков.</summary>
    /// <param name="services">Коллекция сервисов; не может быть <see langword="null"/>.</param>
    /// <returns>Исходная коллекция сервисов.</returns>
    public static IServiceCollection AddBurrfishHealthChecks(this IServiceCollection services)
    {
        services.AddHealthChecks()
            .AddCheck<PostgresHealthCheck>(
                "postgresql",
                failureStatus: HealthStatus.Unhealthy,
                tags: [ReadyTag])
            .AddCheck<WorkerHeartbeatHealthCheck>(
                "workers",
                failureStatus: HealthStatus.Unhealthy,
                tags: [ReadyTag]);
        return services;
    }

    /// <summary>Публикует маршруты живости и готовности без внутренних диагностических данных.</summary>
    /// <param name="endpoints">Построитель маршрутов; не может быть <see langword="null"/>.</param>
    /// <returns>Исходный построитель маршрутов.</returns>
    public static IEndpointRouteBuilder MapBurrfishHealthChecks(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = _ => false
        });
        endpoints.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains(ReadyTag)
        });
        return endpoints;
    }
}
