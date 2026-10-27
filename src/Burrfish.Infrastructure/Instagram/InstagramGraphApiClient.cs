using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Burrfish.Application.Instagram;
using Burrfish.Application.Processing;
using Burrfish.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace Burrfish.Infrastructure.Instagram;

/// <summary>Отправляет текстовые сообщения через Instagram API with Instagram Login.</summary>
public sealed class InstagramGraphApiClient(
    HttpClient httpClient,
    IOptions<InstagramOptions> options,
    TimeProvider timeProvider) : IInstagramMessageSender
{
    private const int MaxResponseBytes = 65_536;

    /// <inheritdoc />
    public async Task<DeliveryResult> SendAsync(
        InstagramReply reply,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var settings = options.Value;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(settings.SendTimeoutSeconds));

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{settings.ApiVersion}/{settings.AccountId}/messages");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.AccessToken);
        request.Content = JsonContent.Create(new
        {
            recipient = new { id = reply.RecipientId },
            message = new { text = reply.Text }
        });

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
                    response.IsSuccessStatusCode ? DeliveryStatus.Failed : StatusFor(response),
                    "ResponseTooLarge",
                    "Meta response exceeded the supported size.",
                    RetryAfter(response));
            }

            return ReadResponse(response, body);
        }
        catch (OperationCanceledException)
            when (!cancellationToken.IsCancellationRequested && timeout.IsCancellationRequested)
        {
            return Failure(
                DeliveryStatus.Retry,
                "Timeout",
                "Meta request timed out.",
                null);
        }
        catch (HttpRequestException exception)
        {
            return Failure(
                DeliveryStatus.Retry,
                nameof(HttpRequestException),
                exception.Message,
                null);
        }
        catch (IOException exception)
        {
            return Failure(
                DeliveryStatus.Retry,
                nameof(IOException),
                exception.Message,
                null);
        }
    }

    private DeliveryResult ReadResponse(HttpResponseMessage response, byte[] body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (response.IsSuccessStatusCode)
            {
                if (document.RootElement.TryGetProperty("message_id", out var messageId)
                    && messageId.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(messageId.GetString()))
                {
                    return new(DeliveryStatus.Sent, messageId.GetString(), null, null, null);
                }

                return Failure(
                    DeliveryStatus.Failed,
                    "InvalidResponse",
                    "Meta response did not contain a message identifier.",
                    null);
            }

            if (document.RootElement.TryGetProperty("error", out var error)
                && error.ValueKind == JsonValueKind.Object)
            {
                var code = ReadCode(error) ?? $"HTTP{(int)response.StatusCode}";
                var message = ReadString(error, "message")
                    ?? $"Meta returned HTTP {(int)response.StatusCode}.";
                var transient = ReadBoolean(error, "is_transient") || IsTransientStatus(response.StatusCode);
                return Failure(
                    transient ? DeliveryStatus.Retry : DeliveryStatus.Failed,
                    code,
                    message,
                    transient ? RetryAfter(response) : null);
            }
        }
        catch (JsonException)
        {
        }

        return Failure(
            StatusFor(response),
            $"HTTP{(int)response.StatusCode}",
            $"Meta returned HTTP {(int)response.StatusCode} with an invalid response.",
            RetryAfter(response));
    }

    private static DeliveryStatus StatusFor(HttpResponseMessage response) =>
        IsTransientStatus(response.StatusCode) ? DeliveryStatus.Retry : DeliveryStatus.Failed;

    private static bool IsTransientStatus(System.Net.HttpStatusCode statusCode) =>
        statusCode is System.Net.HttpStatusCode.RequestTimeout
            or System.Net.HttpStatusCode.TooManyRequests
            || (int)statusCode >= 500;

    private static string? ReadCode(JsonElement error)
    {
        if (!error.TryGetProperty("code", out var code))
        {
            return null;
        }

        return code.ValueKind switch
        {
            JsonValueKind.Number => code.GetRawText(),
            JsonValueKind.String => code.GetString(),
            _ => null
        };
    }

    private static string? ReadString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.String
                ? property.GetString()
                : null;

    private static bool ReadBoolean(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property)
        && property.ValueKind is JsonValueKind.True;

    private TimeSpan? RetryAfter(HttpResponseMessage response)
    {
        if (response.Headers.RetryAfter?.Delta is { } delay && delay > TimeSpan.Zero)
        {
            return delay;
        }

        if (response.Headers.RetryAfter?.Date is { } date)
        {
            var until = date - timeProvider.GetUtcNow();
            return until > TimeSpan.Zero ? until : null;
        }

        return null;
    }

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
