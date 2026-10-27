using Burrfish.Application;
using Burrfish.Application.Instagram;
using Burrfish.Application.OpenAI;
using Burrfish.Application.LeadQualification;
using Burrfish.Application.Persistence;
using Burrfish.Infrastructure.Configuration;
using Burrfish.Infrastructure.Instagram;
using Burrfish.Infrastructure.Persistence;
using Burrfish.Infrastructure.Processing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Npgsql;
using MediatR;

namespace Burrfish.Infrastructure.Tests.Configuration;

public sealed class OptionsValidationTests
{
    public static TheoryData<string, string?, string> InvalidSettings => new()
    {
        { "Instagram:AccountId", null, nameof(InstagramOptions.AccountId) },
        { "Instagram:AppSecret", null, nameof(InstagramOptions.AppSecret) },
        { "Instagram:VerifyToken", null, nameof(InstagramOptions.VerifyToken) },
        { "Instagram:AccessToken", null, nameof(InstagramOptions.AccessToken) },
        { "OpenAI:ApiKey", null, nameof(OpenAiOptions.ApiKey) },
        { "OpenAI:Model", null, nameof(OpenAiOptions.Model) },
        { "OpenAI:TimeoutSeconds", "0", nameof(OpenAiOptions.TimeoutSeconds) },
        { "OpenAI:TimeoutSeconds", "61", nameof(OpenAiOptions.TimeoutSeconds) },
        { "Telegram:BotToken", null, nameof(TelegramOptions.BotToken) },
        { "Telegram:ChatId", null, nameof(TelegramOptions.ChatId) },
        { "Database:ConnectionString", null, nameof(DatabaseOptions.ConnectionString) },
        {
            "Database:ConnectionString",
            "not-a-connection-string",
            nameof(DatabaseOptions.ConnectionString)
        },
        { "Dialog:RetentionDays", "0", nameof(DialogOptions.RetentionDays) },
        {
            "InboxProcessing:PollIntervalMilliseconds",
            "0",
            nameof(InboxProcessingOptions.PollIntervalMilliseconds)
        },
        {
            "InboxProcessing:RetryBaseDelaySeconds",
            "0",
            nameof(InboxProcessingOptions.RetryBaseDelaySeconds)
        },
        {
            "InboxProcessing:MaxRetryDelaySeconds",
            "29",
            nameof(InboxProcessingOptions.MaxRetryDelaySeconds)
        },
        { "Dialog:PriceExamples:0", null, nameof(DialogOptions.PriceExamples) },
        { "Dialog:ServicesExamples:0", null, nameof(DialogOptions.ServicesExamples) },
        { "Dialog:PriceResponse", null, nameof(DialogOptions.PriceResponse) },
        { "Dialog:ServicesResponse", null, nameof(DialogOptions.ServicesResponse) },
        { "Dialog:FallbackResponse", null, nameof(DialogOptions.FallbackResponse) },
        { "Dialog:HandoffOffer", null, nameof(DialogOptions.HandoffOffer) },
        { "Dialog:HandoffConfirmationPrompt", null, nameof(DialogOptions.HandoffConfirmationPrompt) },
        {
            "Dialog:HandoffConfirmationPrompt",
            "Передать запрос менеджеру?",
            nameof(DialogOptions.HandoffConfirmationPrompt)
        },
        {
            "Dialog:AmbiguousConfirmationResponse",
            null,
            nameof(DialogOptions.AmbiguousConfirmationResponse)
        }
    };

    [Fact]
    public async Task Valid_configuration_starts_host()
    {
        using var host = CreateHost(ValidSettings());

        await host.StartAsync(CancellationToken.None);

        await host.StopAsync(CancellationToken.None);
    }

    [Fact]
    public void Valid_configuration_registers_scoped_persistence_services()
    {
        using var host = CreateHost(ValidSettings());
        using var scope = host.Services.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<BurrfishDbContext>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IInboxWriter>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IConversationRepository>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<ILeadRepository>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IOutboxWriter>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IConversationPersistence>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IMetaSignatureVerifier>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IMetaWebhookParser>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<InstagramEventNormalizer>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<InboxProcessor>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IAiDialogClient>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IDialogTemplateProvider>());
        Assert.IsType<ProcessIncomingMessageCommandHandler>(scope.ServiceProvider.GetRequiredService<
            IRequestHandler<ProcessIncomingMessageCommand, ProcessIncomingMessageResult>>());
    }

    [Theory]
    [MemberData(nameof(InvalidSettings))]
    public async Task Invalid_configuration_stops_host_without_exposing_value(
        string key,
        string? invalidValue,
        string expectedProperty)
    {
        var settings = ValidSettings();
        var sensitiveValue = settings[key];
        settings[key] = invalidValue;
        using var host = CreateHost(settings);

        var exception = await Assert.ThrowsAsync<OptionsValidationException>(
            () => host.StartAsync(CancellationToken.None));

        Assert.Contains(expectedProperty, exception.Message, StringComparison.Ordinal);
        var isSensitive = key is "Instagram:AppSecret"
            or "Instagram:VerifyToken"
            or "Instagram:AccessToken"
            or "OpenAI:ApiKey"
            or "Telegram:BotToken"
            or "Database:ConnectionString";
        if (isSensitive && !string.IsNullOrEmpty(sensitiveValue))
        {
            Assert.DoesNotContain(sensitiveValue, exception.Message, StringComparison.Ordinal);
        }
    }

    private static IHost CreateHost(Dictionary<string, string?> settings)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection(settings);
        builder.Services.AddApplication();
        builder.Services.AddInfrastructure(builder.Configuration);

        return builder.Build();
    }

    private static Dictionary<string, string?> ValidSettings() => new()
    {
        ["Instagram:AccountId"] = "instagram-account-id",
        ["Instagram:AppSecret"] = "meta-app-secret-value",
        ["Instagram:VerifyToken"] = "meta-verify-token-value",
        ["Instagram:AccessToken"] = "meta-access-token-value",
        ["OpenAI:ApiKey"] = "openai-api-key-value",
        ["OpenAI:Model"] = "gpt-4.1-mini",
        ["OpenAI:TimeoutSeconds"] = "15",
        ["Telegram:BotToken"] = "telegram-bot-token-value",
        ["Telegram:ChatId"] = "-1001234567890",
        ["Database:ConnectionString"] = new NpgsqlConnectionStringBuilder
        {
            Host = "localhost"
        }.ConnectionString,
        ["Dialog:RetentionDays"] = "30",
        ["Dialog:PriceExamples:0"] = "Сколько стоит?",
        ["Dialog:ServicesExamples:0"] = "Какие услуги вы оказываете?",
        ["Dialog:PriceResponse"] = "Стоимость зависит от вашей задачи.",
        ["Dialog:ServicesResponse"] = "Мы поможем подобрать подходящую услугу.",
        ["Dialog:FallbackResponse"] = "Опишите, пожалуйста, вашу задачу.",
        ["Dialog:HandoffOffer"] = "Передать ваш вопрос менеджеру?",
        ["Dialog:HandoffConfirmationPrompt"] = "Передать менеджеру запрос: {need}?",
        ["Dialog:AmbiguousConfirmationResponse"] = "Ответьте, пожалуйста, да или нет.",
        ["InboxProcessing:Enabled"] = "false",
        ["InboxProcessing:PollIntervalMilliseconds"] = "1000",
        ["InboxProcessing:RetryBaseDelaySeconds"] = "30",
        ["InboxProcessing:MaxRetryDelaySeconds"] = "900"
    };
}
