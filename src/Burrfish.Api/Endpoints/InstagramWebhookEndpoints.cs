using System.Text;
using Burrfish.Application.Instagram;
using Burrfish.Infrastructure.Configuration;
using Burrfish.Infrastructure.Observability;
using MediatR;
using Microsoft.Extensions.Options;

namespace Burrfish.Api.Endpoints;

/// <summary>HTTP-контракт приёма webhook Instagram.</summary>
public static class InstagramWebhookEndpoints
{
    private const int MaxBodyBytes = 1_048_576;
    private const string SignatureHeader = "X-Hub-Signature-256";

    /// <summary>Подтверждает подключение webhook по проверочному токену.</summary>
    /// <param name="request">HTTP-запрос Meta; не может быть <see langword="null"/>.</param>
    /// <param name="options">Настройки Instagram; не могут быть <see langword="null"/>.</param>
    /// <returns>Challenge при успехе или HTTP 403 при отказе.</returns>
    public static IResult Verify(
        HttpRequest request,
        IOptions<InstagramOptions> options)
    {
        var mode = GetSingleQueryValue(request, "hub.mode");
        var token = GetSingleQueryValue(request, "hub.verify_token");
        var challenge = GetSingleQueryValue(request, "hub.challenge");

        if (mode != "subscribe"
            || !string.Equals(token, options.Value.VerifyToken, StringComparison.Ordinal)
            || string.IsNullOrEmpty(challenge))
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        return Results.Text(challenge, "text/plain", Encoding.UTF8);
    }

    /// <summary>Проверяет подпись и ожидает сохранения входящих событий.</summary>
    /// <param name="request">HTTP-запрос Meta; не может быть <see langword="null"/>.</param>
    /// <param name="verifier">Проверяющий подпись; не может быть <see langword="null"/>.</param>
    /// <param name="sender">Отправитель прикладной команды; не может быть <see langword="null"/>.</param>
    /// <param name="cancellationToken">Токен отмены запроса.</param>
    /// <returns>HTTP-результат приёма webhook.</returns>
    public static async Task<IResult> Receive(
        HttpRequest request,
        IMetaSignatureVerifier verifier,
        ISender sender,
        BurrfishMetrics metrics,
        CancellationToken cancellationToken)
    {
        if (!request.Headers.TryGetValue(SignatureHeader, out var signatures)
            || signatures.Count != 1)
        {
            metrics.RecordEvent(EventOutcome.Rejected);
            return Results.StatusCode(StatusCodes.Status401Unauthorized);
        }

        if (request.ContentLength is > MaxBodyBytes)
        {
            metrics.RecordEvent(EventOutcome.Rejected);
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        }

        var body = await ReadBodyAsync(request.Body, cancellationToken);
        if (body is null)
        {
            metrics.RecordEvent(EventOutcome.Rejected);
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        }

        if (!verifier.IsValid(body, signatures[0]))
        {
            metrics.RecordEvent(EventOutcome.Rejected);
            return Results.StatusCode(StatusCodes.Status401Unauthorized);
        }

        var accepted = await sender.Send(
            new ReceiveInstagramWebhookCommand(body, DateTimeOffset.UtcNow),
            cancellationToken);

        metrics.RecordEvent(accepted ? EventOutcome.Accepted : EventOutcome.Rejected);
        return accepted ? Results.Ok() : Results.BadRequest();
    }

    private static string? GetSingleQueryValue(HttpRequest request, string name) =>
        request.Query.TryGetValue(name, out var values) && values.Count == 1
            ? values[0]
            : null;

    private static async Task<byte[]?> ReadBodyAsync(
        Stream source,
        CancellationToken cancellationToken)
    {
        using var destination = new MemoryStream();
        var buffer = new byte[8192];

        while (true)
        {
            var remaining = MaxBodyBytes + 1 - (int)destination.Length;
            var count = await source.ReadAsync(
                buffer.AsMemory(0, Math.Min(buffer.Length, remaining)),
                cancellationToken);

            if (count == 0)
            {
                return destination.ToArray();
            }

            destination.Write(buffer, 0, count);
            if (destination.Length > MaxBodyBytes)
            {
                return null;
            }
        }
    }
}
