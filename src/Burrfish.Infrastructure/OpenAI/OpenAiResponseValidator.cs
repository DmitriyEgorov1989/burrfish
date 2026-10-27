using System.Text.Json;
using Burrfish.Application.OpenAI;

namespace Burrfish.Infrastructure.OpenAI;

/// <summary>Проверяет закрытую структуру решения модели.</summary>
public sealed class OpenAiResponseValidator
{
    private const int MaxTextLength = 1_000;

    /// <summary>Проверяет JSON одного решения.</summary>
    /// <param name="json">Структурированный ответ модели; не может быть <see langword="null"/>.</param>
    /// <returns>Проверенное решение или ошибка валидации.</returns>
    public AiDialogResult Validate(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                return Invalid();
            }

            var propertyNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in root.EnumerateObject())
            {
                if (!propertyNames.Add(property.Name)
                    || property.Name is not ("kind" or "text"))
                {
                    return Invalid();
                }
            }

            if (propertyNames.Count != 2
                || !root.TryGetProperty("kind", out var kindElement)
                || kindElement.ValueKind != JsonValueKind.String
                || !root.TryGetProperty("text", out var textElement))
            {
                return Invalid();
            }

            var kind = kindElement.GetString() switch
            {
                "price" => AiDialogDecisionKind.Price,
                "services" => AiDialogDecisionKind.Services,
                "ask_clarifying_question" => AiDialogDecisionKind.AskClarifyingQuestion,
                "propose_handoff" => AiDialogDecisionKind.ProposeHandoff,
                _ => (AiDialogDecisionKind?)null
            };

            if (kind is null)
            {
                return Invalid();
            }

            if (kind is AiDialogDecisionKind.Price or AiDialogDecisionKind.Services)
            {
                return textElement.ValueKind == JsonValueKind.Null
                    ? AiDialogResult.Success(new AiDialogDecision(kind.Value, null))
                    : Invalid();
            }

            if (textElement.ValueKind != JsonValueKind.String)
            {
                return Invalid();
            }

            var text = textElement.GetString();
            if (string.IsNullOrWhiteSpace(text) || text.Length > MaxTextLength)
            {
                return Invalid();
            }

            return AiDialogResult.Success(new AiDialogDecision(kind.Value, text));
        }
        catch (JsonException)
        {
            return Invalid();
        }
    }

    private static AiDialogResult Invalid() =>
        AiDialogResult.Failure(new AiDialogError(
            "InvalidDecision",
            "OpenAI returned an invalid dialog decision.",
            null,
            false));
}
