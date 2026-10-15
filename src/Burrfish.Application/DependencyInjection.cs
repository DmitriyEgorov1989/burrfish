using Microsoft.Extensions.DependencyInjection;

namespace Burrfish.Application;

/// <summary>Регистрирует прикладные сценарии и CQRS handlers.</summary>
public static class DependencyInjection
{
    /// <summary>Добавляет MediatR handlers из сборки Application.</summary>
    /// <param name="services">Коллекция сервисов; не может быть <see langword="null"/>.</param>
    /// <returns>Исходная коллекция сервисов.</returns>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddMediatR(configuration =>
            configuration.RegisterServicesFromAssembly(typeof(DependencyInjection).Assembly));

        return services;
    }
}
