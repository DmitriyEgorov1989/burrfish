using Burrfish.Application.LeadQualification;
using Burrfish.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace Burrfish.Infrastructure.LeadQualification;

/// <summary>Преобразует конфигурацию в утверждённые прикладные шаблоны.</summary>
public sealed class ConfiguredDialogTemplateProvider(IOptions<DialogOptions> options)
    : IDialogTemplateProvider
{
    /// <inheritdoc />
    public DialogTemplates Get()
    {
        var settings = options.Value;
        return new(
            settings.PriceResponse,
            settings.ServicesResponse,
            settings.FallbackResponse,
            settings.HandoffOffer,
            settings.HandoffConfirmationPrompt,
            settings.AmbiguousConfirmationResponse);
    }
}
