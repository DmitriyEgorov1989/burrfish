using Burrfish.Infrastructure.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

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
        { "Telegram:BotToken", null, nameof(TelegramOptions.BotToken) },
        { "Telegram:ChatId", null, nameof(TelegramOptions.ChatId) },
        { "Dialog:RetentionDays", "0", nameof(DialogOptions.RetentionDays) },
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
            or "Telegram:BotToken";
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
        ["Telegram:BotToken"] = "telegram-bot-token-value",
        ["Telegram:ChatId"] = "-1001234567890",
        ["Dialog:RetentionDays"] = "30",
        ["Dialog:PriceExamples:0"] = "Сколько стоит?",
        ["Dialog:ServicesExamples:0"] = "Какие услуги вы оказываете?",
        ["Dialog:PriceResponse"] = "Стоимость зависит от вашей задачи.",
        ["Dialog:ServicesResponse"] = "Мы поможем подобрать подходящую услугу.",
        ["Dialog:FallbackResponse"] = "Опишите, пожалуйста, вашу задачу.",
        ["Dialog:HandoffOffer"] = "Передать ваш вопрос менеджеру?",
        ["Dialog:HandoffConfirmationPrompt"] = "Передать менеджеру запрос: {need}?",
        ["Dialog:AmbiguousConfirmationResponse"] = "Ответьте, пожалуйста, да или нет."
    };
}
