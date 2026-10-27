namespace Burrfish.Application.LeadQualification;

/// <summary>Утверждённые тексты автоматических ответов.</summary>
/// <param name="PriceResponse">Ответ о цене; не может быть <see langword="null"/>.</param>
/// <param name="ServicesResponse">Ответ об услугах; не может быть <see langword="null"/>.</param>
/// <param name="FallbackResponse">Резервный ответ; не может быть <see langword="null"/>.</param>
/// <param name="HandoffOffer">Предложение передачи; не может быть <see langword="null"/>.</param>
/// <param name="HandoffConfirmationPrompt">Шаблон с маркером <c>{need}</c>; не может быть <see langword="null"/>.</param>
/// <param name="AmbiguousConfirmationResponse">Ответ при неоднозначном подтверждении; не может быть <see langword="null"/>.</param>
public sealed record DialogTemplates(
    string PriceResponse,
    string ServicesResponse,
    string FallbackResponse,
    string HandoffOffer,
    string HandoffConfirmationPrompt,
    string AmbiguousConfirmationResponse);
