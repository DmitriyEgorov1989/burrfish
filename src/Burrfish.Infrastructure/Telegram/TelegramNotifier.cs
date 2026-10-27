using System.Net.Http.Json;
using System.Text.Json;
using Burrfish.Application.Processing;
using Burrfish.Application.Telegram;
using Burrfish.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace Burrfish.Infrastructure.Telegram;

/// <summary>Отправляет уведомления менеджерам через Telegram Bot API.</summary>
public sealed class TelegramNotifier(
    HttpClient httpClient,
    IOptions<TelegramOptions> options,
    TelegramLeadMessageFormatter formatter) : ITelegramNotifier
{
    private const int MaxResponseBytes = 65_536;

    /// <inheritdoc />
    public async Task<DeliveryResult> SendLeadAsync(
        LeadNotification notification,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var settings = options.Value;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(settings.TimeoutSeconds));
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"bot{settings.BotToken}/sendMessage")
        {
            Content = JsonContent.Create(new
            {
                chat_id = settings.ChatId,
                text = formatter.Format(notification),
                parse_mode = "HTML",
                link_preview_options = new { is_disabled = true }
            })
        };

        try
        {
            using var response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                timeout.Token);
            var body = await ReadBoundedAsync(response.Content, timeout.Token);
            if (body is null)
            {
                return Failure(
                    IsTransient(response.StatusCode) ? DeliveryStatus.Retry : DeliveryStatus.Failed,
                    "ResponseTooLarge",
                    "Telegram response exceeded the supported size.",
                    null);
            }

            return ReadResponse(response, body);
        }
        catch (OperationCanceledException)
            when (!cancellationToken.IsCancellationRequested && timeout.IsCancellationRequested)
        {
            return Failure(DeliveryStatus.Retry, "Timeout", "Telegram request timed out.", null);
        }
        catch (HttpRequestException exception)
        {
            return Failure(
                DeliveryStatus.Retry,
                nameof(HttpRequestException),
                Redact(exception.Message, settings.BotToken),
                null);
        }
        catch (IOException exception)
        {
            return Failure(
                DeliveryStatus.Retry,
                nameof(IOException),
                Redact(exception.Message, settings.BotToken),
                null);
        }
    }

    private static DeliveryResult ReadResponse(HttpResponseMessage response, byte[] body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return Failure(
                    IsTransient(response.StatusCode) ? DeliveryStatus.Retry : DeliveryStatus.Failed,
                    $"HTTP{(int)response.StatusCode}",
                    $"Telegram returned HTTP {(int)response.StatusCode} with an invalid response.",
                    null);
            }

            if (response.IsSuccessStatusCode && ReadBoolean(root, "ok"))
            {
                if (root.TryGetProperty("result", out var result)
                    && result.ValueKind == JsonValueKind.Object
                    && result.TryGetProperty("message_id", out var messageId)
                    && messageId.ValueKind == JsonValueKind.Number)
                {
                    return new(
                        DeliveryStatus.Sent,
                        messageId.GetRawText(),
                        null,
                        null,
                        null);
                }

                return Failure(
                    DeliveryStatus.Failed,
                    "InvalidResponse",
                    "Telegram response did not contain a message identifier.",
                    null);
            }

            var errorCode = ReadInteger(root, "error_code")
                ?? (int)response.StatusCode;
            var description = ReadString(root, "description")
                ?? $"Telegram returned HTTP {(int)response.StatusCode}.";
            var retryAfter = ReadRetryAfter(root);
            var transient = errorCode == 429
                || retryAfter is not null
                || IsTransient(response.StatusCode);
            return Failure(
                transient ? DeliveryStatus.Retry : DeliveryStatus.Failed,
                errorCode.ToString(),
                description,
                retryAfter);
        }
        catch (JsonException)
        {
            return Failure(
                IsTransient(response.StatusCode) ? DeliveryStatus.Retry : DeliveryStatus.Failed,
                $"HTTP{(int)response.StatusCode}",
                $"Telegram returned HTTP {(int)response.StatusCode} with an invalid response.",
                null);
        }
    }

    private static TimeSpan? ReadRetryAfter(JsonElement root)
    {
        if (root.TryGetProperty("parameters", out var parameters)
            && parameters.ValueKind == JsonValueKind.Object
            && ReadInteger(parameters, "retry_after") is > 0 and var seconds)
        {
            return TimeSpan.FromSeconds(seconds);
        }

        return null;
    }

    private static bool IsTransient(System.Net.HttpStatusCode statusCode) =>
        statusCode is System.Net.HttpStatusCode.RequestTimeout
            or System.Net.HttpStatusCode.TooManyRequests
            || (int)statusCode >= 500;

    private static bool ReadBoolean(JsonElement element, string name) =>
        element.TryGetProperty(name, out var property)
        && property.ValueKind == JsonValueKind.True;

    private static int? ReadInteger(JsonElement element, string name) =>
        element.TryGetProperty(name, out var property)
            && property.ValueKind == JsonValueKind.Number
            && property.TryGetInt32(out var value)
                ? value
                : null;

    private static string? ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var property)
            && property.ValueKind == JsonValueKind.String
                ? property.GetString()
                : null;

    private static string Redact(string message, string token) =>
        string.IsNullOrEmpty(token)
            ? message
            : message.Replace(token, "[REDACTED]", StringComparison.Ordinal);

    private static DeliveryResult Failure(
        DeliveryStatus status,
        string code,
        string message,
        TimeSpan? retryAfter) => new(status, null, code, message, retryAfter);

    private static async Task<byte[]?> ReadBoundedAsync(
        HttpContent content,
        CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength is > MaxResponseBytes)
        {
            return null;
        }

        await using var source = await content.ReadAsStreamAsync(cancellationToken);
        using var destination = new MemoryStream();
        var buffer = new byte[8_192];
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                return destination.ToArray();
            }

            if (destination.Length + read > MaxResponseBytes)
            {
                return null;
            }

            destination.Write(buffer, 0, read);
        }
    }
}
