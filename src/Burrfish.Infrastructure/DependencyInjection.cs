using Burrfish.Application.Persistence;
using Burrfish.Application.Instagram;
using Burrfish.Application.OpenAI;
using Burrfish.Application.LeadQualification;
using Burrfish.Application.Telegram;
using Burrfish.Infrastructure.Configuration;
using Burrfish.Infrastructure.Instagram;
using Burrfish.Infrastructure.LeadQualification;
using Burrfish.Infrastructure.OpenAI;
using Burrfish.Infrastructure.Observability;
using Burrfish.Infrastructure.Persistence;
using Burrfish.Infrastructure.Processing;
using Burrfish.Infrastructure.Telegram;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;

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
        services.AddOptions<DatabaseOptions>()
            .Bind(configuration.GetSection(DatabaseOptions.SectionName))
            .ValidateRequired(options => options.ConnectionString, nameof(DatabaseOptions.ConnectionString))
            .Validate(
                options => IsValidConnectionString(options.ConnectionString),
                $"{nameof(DatabaseOptions.ConnectionString)} is invalid.")
            .ValidateOnStart();

        services.AddOptions<InstagramOptions>()
            .Bind(configuration.GetSection(InstagramOptions.SectionName))
            .ValidateRequired(options => options.AccountId, nameof(InstagramOptions.AccountId))
            .ValidateRequired(options => options.AppSecret, nameof(InstagramOptions.AppSecret))
            .ValidateRequired(options => options.VerifyToken, nameof(InstagramOptions.VerifyToken))
            .ValidateRequired(options => options.AccessToken, nameof(InstagramOptions.AccessToken))
            .ValidateRequired(options => options.ApiVersion, nameof(InstagramOptions.ApiVersion))
            .Validate(
                options => IsValidApiVersion(options.ApiVersion),
                $"{nameof(InstagramOptions.ApiVersion)} must use the vNN.0 format.")
            .Validate(
                options => options.SendTimeoutSeconds is >= 1 and <= 60,
                $"{nameof(InstagramOptions.SendTimeoutSeconds)} must be between 1 and 60 seconds.")
            .ValidateOnStart();

        services.AddDbContext<BurrfishDbContext>((provider, options) =>
        {
            var database = provider.GetRequiredService<IOptions<DatabaseOptions>>().Value;
            options.UseNpgsql(database.ConnectionString);
            options.EnableSensitiveDataLogging(false);
        });
        services.AddScoped<IInboxWriter, InboxWriter>();
        services.AddScoped<IConversationRepository, ConversationRepository>();
        services.AddScoped<ILeadRepository, LeadRepository>();
        services.AddScoped<IOutboxWriter, OutboxWriter>();
        services.AddScoped<IConversationPersistence, ConversationPersistence>();
        services.AddSingleton<IMetaSignatureVerifier, MetaSignatureVerifier>();
        services.AddSingleton<IMetaWebhookParser, MetaWebhookParser>();
        services.AddSingleton<InstagramEventNormalizer>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<WorkerHeartbeat>();
        services.AddSingleton<BurrfishMetrics>();
        services.AddSingleton<IDialogTemplateProvider, ConfiguredDialogTemplateProvider>();
        services.AddScoped<InboxProcessor>();
        services.AddScoped<OutboxDispatcher>();
        services.AddScoped<DataRetentionService>();
        services.AddScoped<QueueMetricsCollector>();
        services.AddHttpClient<IInstagramMessageSender, InstagramGraphApiClient>(client =>
            {
                client.BaseAddress = new Uri("https://graph.instagram.com/");
                client.Timeout = Timeout.InfiniteTimeSpan;
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AllowAutoRedirect = false
            })
            .RedactLoggedHeaders(_ => true);
        services.AddSingleton<TelegramLeadMessageFormatter>();
        services.AddHttpClient<ITelegramNotifier, TelegramNotifier>(client =>
            {
                client.BaseAddress = new Uri("https://api.telegram.org/");
                client.Timeout = Timeout.InfiniteTimeSpan;
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AllowAutoRedirect = false
            })
            .RemoveAllLoggers();

        services.AddOptions<InboxProcessingOptions>()
            .Bind(configuration.GetSection(InboxProcessingOptions.SectionName))
            .Validate(
                options => options.PollIntervalMilliseconds > 0,
                $"{nameof(InboxProcessingOptions.PollIntervalMilliseconds)} must be positive.")
            .Validate(
                options => options.RetryBaseDelaySeconds > 0,
                $"{nameof(InboxProcessingOptions.RetryBaseDelaySeconds)} must be positive.")
            .Validate(
                options => options.MaxRetryDelaySeconds >= options.RetryBaseDelaySeconds,
                $"{nameof(InboxProcessingOptions.MaxRetryDelaySeconds)} must not be less than {nameof(InboxProcessingOptions.RetryBaseDelaySeconds)}.")
            .ValidateOnStart();

        services.AddOptions<OutboxProcessingOptions>()
            .Bind(configuration.GetSection(OutboxProcessingOptions.SectionName))
            .Validate(
                options => options.PollIntervalMilliseconds > 0,
                $"{nameof(OutboxProcessingOptions.PollIntervalMilliseconds)} must be positive.")
            .Validate(
                options => options.RetryBaseDelaySeconds > 0,
                $"{nameof(OutboxProcessingOptions.RetryBaseDelaySeconds)} must be positive.")
            .Validate(
                options => options.MaxRetryDelaySeconds >= options.RetryBaseDelaySeconds,
                $"{nameof(OutboxProcessingOptions.MaxRetryDelaySeconds)} must not be less than {nameof(OutboxProcessingOptions.RetryBaseDelaySeconds)}.")
            .ValidateOnStart();

        services.AddOptions<DataRetentionOptions>()
            .Bind(configuration.GetSection(DataRetentionOptions.SectionName))
            .Validate(
                options => options.IntervalMinutes > 0,
                $"{nameof(DataRetentionOptions.IntervalMinutes)} must be positive.")
            .ValidateOnStart();

        services.AddOptions<ObservabilityOptions>()
            .Bind(configuration.GetSection(ObservabilityOptions.SectionName))
            .Validate(
                options => options.QueueRefreshIntervalSeconds > 0,
                $"{nameof(ObservabilityOptions.QueueRefreshIntervalSeconds)} must be positive.")
            .ValidateOnStart();

        services.AddOptions<OpenAiOptions>()
            .Bind(configuration.GetSection(OpenAiOptions.SectionName))
            .ValidateRequired(options => options.ApiKey, nameof(OpenAiOptions.ApiKey))
            .ValidateRequired(options => options.Model, nameof(OpenAiOptions.Model))
            .Validate(
                options => options.TimeoutSeconds is >= 1 and <= 60,
                $"{nameof(OpenAiOptions.TimeoutSeconds)} must be between 1 and 60 seconds.")
            .ValidateOnStart();

        services.AddSingleton<OpenAiRequestBuilder>();
        services.AddSingleton<OpenAiResponseValidator>();
        services.AddSingleton<OpenAiResponseReader>();
        services.AddHttpClient<IAiDialogClient, OpenAiDialogClient>(client =>
            {
                client.BaseAddress = new Uri("https://api.openai.com/v1/");
                client.Timeout = Timeout.InfiniteTimeSpan;
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AllowAutoRedirect = false
            })
            .RedactLoggedHeaders(_ => true);

        services.AddOptions<TelegramOptions>()
            .Bind(configuration.GetSection(TelegramOptions.SectionName))
            .ValidateRequired(options => options.BotToken, nameof(TelegramOptions.BotToken))
            .Validate(options => options.ChatId != 0, $"{nameof(TelegramOptions.ChatId)} is required.")
            .Validate(
                options => options.TimeoutSeconds is >= 1 and <= 60,
                $"{nameof(TelegramOptions.TimeoutSeconds)} must be between 1 and 60 seconds.")
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

    private static bool IsValidConnectionString(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        try
        {
            _ = new NpgsqlConnectionStringBuilder(value);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static bool IsValidApiVersion(string value) =>
        value is { Length: >= 5 }
        && value[0] == 'v'
        && value.EndsWith(".0", StringComparison.Ordinal)
        && value.AsSpan(1, value.Length - 3).IndexOfAnyExceptInRange('0', '9') < 0;
}
