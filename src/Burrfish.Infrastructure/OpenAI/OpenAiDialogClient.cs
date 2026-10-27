using System.Net.Http.Headers;
using Burrfish.Application.OpenAI;
using Burrfish.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace Burrfish.Infrastructure.OpenAI;

/// <summary>Обращается к Responses API и возвращает проверенное решение.</summary>
public sealed class OpenAiDialogClient(
    HttpClient httpClient,
    IOptions<OpenAiOptions> options,
    OpenAiRequestBuilder requestBuilder,
    OpenAiResponseReader responseReader) : IAiDialogClient
{
    private const int MaxResponseBytes = 65_536;

    /// <inheritdoc />
    public async Task<AiDialogResult> DecideAsync(
        AiDialogRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!IsValidInput(request))
        {
            return Failure(
                "InvalidInput",
                "Dialog input exceeds the supported limits.",
                false);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.Value.TimeoutSeconds));

        using var message = new HttpRequestMessage(HttpMethod.Post, "responses");
        message.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            options.Value.ApiKey);
        message.Content = requestBuilder.Create(request, options.Value.Model);

        int? responseStatusCode = null;
        try
        {
            using var response = await httpClient.SendAsync(
                message,
                HttpCompletionOption.ResponseHeadersRead,
                timeout.Token);
            responseStatusCode = (int)response.StatusCode;
            var body = await ReadBoundedAsync(response.Content, timeout.Token);
            if (body is null)
            {
                return AiDialogResult.Failure(new AiDialogError(
                    "ResponseTooLarge",
                    "OpenAI response exceeded the supported size.",
                    responseStatusCode,
                    IsTransientStatus(responseStatusCode.Value)));
            }

            return responseReader.Read(body, responseStatusCode.Value);
        }
        catch (OperationCanceledException)
            when (!cancellationToken.IsCancellationRequested && timeout.IsCancellationRequested)
        {
            return Failure("Timeout", "OpenAI request timed out.", true);
        }
        catch (HttpRequestException exception)
        {
            return AiDialogResult.Failure(new AiDialogError(
                exception.HttpRequestError.ToString(),
                exception.Message,
                exception.StatusCode is null ? responseStatusCode : (int)exception.StatusCode,
                true));
        }
        catch (IOException exception)
        {
            return AiDialogResult.Failure(new AiDialogError(
                nameof(IOException),
                exception.Message,
                responseStatusCode,
                true));
        }
    }

    private static bool IsValidInput(AiDialogRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Text)
            || request.Text.Length > 4_000
            || request.History is null
            || request.History.Count > 6)
        {
            return false;
        }

        return request.History.All(message =>
            message is not null
            && Enum.IsDefined(message.Role)
            && !string.IsNullOrWhiteSpace(message.Text)
            && message.Text.Length <= 1_000);
    }

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
            var remaining = MaxResponseBytes + 1 - (int)destination.Length;
            var count = await source.ReadAsync(
                buffer.AsMemory(0, Math.Min(buffer.Length, remaining)),
                cancellationToken);
            if (count == 0)
            {
                return destination.ToArray();
            }

            destination.Write(buffer, 0, count);
            if (destination.Length > MaxResponseBytes)
            {
                return null;
            }
        }
    }

    private static AiDialogResult Failure(
        string code,
        string message,
        bool transient) =>
        AiDialogResult.Failure(new AiDialogError(
            code,
            message,
            null,
            transient));

    private static bool IsTransientStatus(int statusCode) =>
        statusCode is 408 or 429 or >= 500 and <= 599;
}
