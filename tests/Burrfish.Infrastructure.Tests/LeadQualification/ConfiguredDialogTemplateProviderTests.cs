using Burrfish.Application.LeadQualification;
using Burrfish.Infrastructure.Configuration;
using Burrfish.Infrastructure.LeadQualification;
using Microsoft.Extensions.Options;

namespace Burrfish.Infrastructure.Tests.LeadQualification;

public sealed class ConfiguredDialogTemplateProviderTests
{
    [Fact]
    public void Configured_templates_preserve_all_approved_texts()
    {
        var options = new DialogOptions
        {
            PriceResponse = "Цена из конфигурации",
            ServicesResponse = "Услуги из конфигурации",
            FallbackResponse = "Резервный ответ",
            HandoffOffer = "Предложение передачи",
            HandoffConfirmationPrompt = "Передать {need}?",
            AmbiguousConfirmationResponse = "Уточнение согласия"
        };
        var provider = new ConfiguredDialogTemplateProvider(Options.Create(options));

        Assert.Equal(new DialogTemplates(
            "Цена из конфигурации", "Услуги из конфигурации", "Резервный ответ",
            "Предложение передачи", "Передать {need}?", "Уточнение согласия"), provider.Get());
    }
}
