using Burrfish.Infrastructure.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Burrfish.Infrastructure;

/// <summary>Регистрирует настройки и инфраструктурные адаптеры.</summary>
public static class DependencyInjection
{
    /// <summary>Добавляет инфраструктуру и проверяет настройки при запуске приложения.</summary>
    /// <param name="services">Коллекция сервисов; не может быть <see langword="null"/>.</param>
    /// <param name="configuration">Конфигурация приложения; не может быть <see langword="null"/>.</param>
    /// <returns>Исходная коллекция сервисов.</returns>
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<InstagramOptions>()
            .Bind(configuration.GetSection(InstagramOptions.SectionName))
            .ValidateRequired(options => options.AccountId, nameof(InstagramOptions.AccountId))
            .ValidateRequired(options => options.AppSecret, nameof(InstagramOptions.AppSecret))
            .ValidateRequired(options => options.VerifyToken, nameof(InstagramOptions.VerifyToken))
            .ValidateRequired(options => options.AccessToken, nameof(InstagramOptions.AccessToken))
            .ValidateOnStart();

        services.AddOptions<OpenAiOptions>()
            .Bind(configuration.GetSection(OpenAiOptions.SectionName))
            .ValidateRequired(options => options.ApiKey, nameof(OpenAiOptions.ApiKey))
            .ValidateRequired(options => options.Model, nameof(OpenAiOptions.Model))
            .ValidateOnStart();

        services.AddOptions<TelegramOptions>()
            .Bind(configuration.GetSection(TelegramOptions.SectionName))
            .ValidateRequired(options => options.BotToken, nameof(TelegramOptions.BotToken))
            .Validate(options => options.ChatId != 0, $"{nameof(TelegramOptions.ChatId)} is required.")
            .ValidateOnStart();

        services.AddOptions<DialogOptions>()
            .Bind(configuration.GetSection(DialogOptions.SectionName))
            .Validate(
                options => options.RetentionDays == 30,
                $"{nameof(DialogOptions.RetentionDays)} must be 30 calendar days.")
            .ValidateExamples(options => options.PriceExamples, nameof(DialogOptions.PriceExamples))
            .ValidateExamples(options => options.ServicesExamples, nameof(DialogOptions.ServicesExamples))
            .ValidateRequired(options => options.PriceResponse, nameof(DialogOptions.PriceResponse))
            .ValidateRequired(options => options.ServicesResponse, nameof(DialogOptions.ServicesResponse))
            .ValidateRequired(options => options.FallbackResponse, nameof(DialogOptions.FallbackResponse))
            .ValidateRequired(options => options.HandoffOffer, nameof(DialogOptions.HandoffOffer))
            .ValidateRequired(
                options => options.HandoffConfirmationPrompt,
                nameof(DialogOptions.HandoffConfirmationPrompt))
            .Validate(
                options => options.HandoffConfirmationPrompt is { } prompt
                    && prompt.Contains("{need}", StringComparison.Ordinal),
                $"{nameof(DialogOptions.HandoffConfirmationPrompt)} must contain {{need}}.")
            .ValidateRequired(
                options => options.AmbiguousConfirmationResponse,
                nameof(DialogOptions.AmbiguousConfirmationResponse))
            .ValidateOnStart();

        return services;
    }

    private static OptionsBuilder<TOptions> ValidateRequired<TOptions>(
        this OptionsBuilder<TOptions> builder,
        Func<TOptions, string> selector,
        string propertyName)
        where TOptions : class => builder.Validate(
            options => !string.IsNullOrWhiteSpace(selector(options)),
            $"{propertyName} is required.");

    private static OptionsBuilder<TOptions> ValidateExamples<TOptions>(
        this OptionsBuilder<TOptions> builder,
        Func<TOptions, string[]> selector,
        string propertyName)
        where TOptions : class => builder.Validate(
            options => selector(options) is { Length: > 0 } examples
                && examples.All(example => !string.IsNullOrWhiteSpace(example)),
            $"{propertyName} must contain at least one non-empty example.");
}
