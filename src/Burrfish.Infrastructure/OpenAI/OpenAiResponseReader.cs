using System.Text.Json;
using Burrfish.Application.OpenAI;

namespace Burrfish.Infrastructure.OpenAI;

/// <summary>Извлекает решение или ошибку из ответа Responses API.</summary>
public sealed class OpenAiResponseReader(OpenAiResponseValidator validator)
{
    /// <summary>Разбирает ограниченное по размеру тело HTTP-ответа.</summary>
    /// <param name="body">Байты ответа; не могут быть <see langword="null"/>.</param>
    /// <param name="statusCode">HTTP-статус ответа.</param>
    /// <returns>Проверенное решение или ошибка интеграции.</returns>
    public AiDialogResult Read(byte[] body, int statusCode)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;

            if (statusCode is < 200 or >= 300)
            {
                return ReadError(root, statusCode);
            }

            if (GetString(root, "status") == "failed")
            {
                return ReadError(root, statusCode);
            }

            if (GetString(root, "status") != "completed")
            {
                return Failure(
                    "IncompleteResponse",
                    "OpenAI response did not complete.",
                    statusCode);
            }

            if (!TryProperty(root, "output", out var output)
                || output.ValueKind != JsonValueKind.Array)
            {
                return Invalid(statusCode);
            }

            var message = default(JsonElement);
            var messageCount = 0;
            foreach (var item in output.EnumerateArray())
            {
                if (GetString(item, "type") == "message")
                {
                    message = item;
                    messageCount++;
                }
            }

            if (messageCount != 1
                || GetString(message, "role") != "assistant"
                || !TryProperty(message, "content", out var content)
                || content.ValueKind != JsonValueKind.Array
                || content.GetArrayLength() != 1)
            {
                return Invalid(statusCode);
            }

            var part = content[0];
            var type = GetString(part, "type");
            if (type == "refusal")
            {
                return Failure(
                    "ModelRefusal",
                    "OpenAI refused to produce a dialog decision.",
                    statusCode);
            }

            if (type != "output_text")
            {
                return Invalid(statusCode);
            }

            var text = GetString(part, "text");
            if (text is null)
            {
                return Invalid(statusCode);
            }

            var result = validator.Validate(text);
            return result.Error is { HttpStatusCode: null } error
                ? Failure(
                    error.Code,
                    error.Message,
                    statusCode,
                    error.IsTransient)
                : result;
        }
        catch (JsonException)
        {
            return statusCode is < 200 or >= 300
                ? Failure(
                    "OpenAiHttpError",
                    "OpenAI returned an unreadable error response.",
                    statusCode,
                    IsTransientStatus(statusCode))
                : Invalid(statusCode);
        }
    }

    private static AiDialogResult ReadError(JsonElement root, int statusCode)
    {
        TryProperty(root, "error", out var error);
        var code = GetString(error, "code")
            ?? GetString(error, "type")
            ?? "OpenAiHttpError";
        var message = GetString(error, "message")
            ?? "OpenAI returned an error.";
        var type = GetString(error, "type");
        var billingError = type == "insufficient_quota"
            || code is "insufficient_quota"
                or "credit_balance_exhausted"
                or "organization_spend_limit_exceeded"
                or "project_spend_limit_exceeded"
                or "organization_usage_limit_exceeded";

        return Failure(
            code,
            message,
            statusCode,
            !billingError && IsTransientStatus(statusCode));
    }

    private static bool IsTransientStatus(int statusCode) =>
        statusCode is 408 or 429 or >= 500 and <= 599;

    private static bool TryProperty(
        JsonElement element,
        string name,
        out JsonElement value)
    {
        value = default;
        return element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(name, out value);
    }

    private static string? GetString(JsonElement element, string name) =>
        TryProperty(element, name, out var value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;

    private static AiDialogResult Invalid(int statusCode) =>
        Failure(
            "InvalidResponse",
            "OpenAI returned an invalid response envelope.",
            statusCode);

    private static AiDialogResult Failure(
        string code,
        string message,
        int statusCode,
        bool transient = false) =>
        AiDialogResult.Failure(new AiDialogError(
            code,
            message,
            statusCode,
            transient));
}
