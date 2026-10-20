using System.Text.Json;
using Burrfish.Application.Instagram;
using Burrfish.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace Burrfish.Infrastructure.Instagram;

/// <summary>Преобразует отдельное inbox-событие в текстовое сообщение.</summary>
public sealed class InstagramEventNormalizer(IOptions<InstagramOptions> options)
{
    /// <summary>Нормализует отдельное событие без сетевых обращений.</summary>
    /// <param name="payload">JSON одного события; не может быть <see langword="null"/>.</param>
    /// <returns>Входящее сообщение либо <see langword="null"/> для некорректного или неподдерживаемого события.</returns>
    public IncomingInstagramMessage? Normalize(string payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;

            if (!TryString(root, "object", out var objectName)
                || objectName != "instagram"
                || !TrySingleItem(root, "entry", out var entry)
                || !TryString(entry, "id", out var accountId)
                || accountId != options.Value.AccountId
                || !TrySingleItem(entry, "messaging", out var item)
                || !TryProperty(item, "sender", out var sender)
                || !TryString(sender, "id", out var senderId)
                || senderId == accountId
                || !TryProperty(item, "recipient", out var recipient)
                || !TryString(recipient, "id", out var recipientId)
                || recipientId != accountId
                || !TryProperty(item, "message", out var message))
            {
                return null;
            }

            if (message.TryGetProperty("is_echo", out var echo))
            {
                if (echo.ValueKind != JsonValueKind.False)
                {
                    return null;
                }
            }

            if (!TryString(message, "mid", out var messageId)
                || !TryString(message, "text", out var text)
                || !TryProperty(item, "timestamp", out var timestamp)
                || timestamp.ValueKind != JsonValueKind.Number
                || !timestamp.TryGetInt64(out var milliseconds))
            {
                return null;
            }

            DateTimeOffset receivedAt;
            try
            {
                receivedAt = DateTimeOffset.FromUnixTimeMilliseconds(milliseconds);
            }
            catch (ArgumentOutOfRangeException)
            {
                return null;
            }

            return new IncomingInstagramMessage(
                messageId,
                $"{accountId}:{senderId}",
                senderId,
                null,
                text,
                receivedAt);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static bool TryProperty(
        JsonElement element,
        string name,
        out JsonElement value)
    {
        value = default;
        return element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(name, out value);
    }

    private static bool TryString(
        JsonElement element,
        string name,
        out string value)
    {
        value = string.Empty;
        if (!TryProperty(element, name, out var property)
            || property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(value);
    }

    private static bool TrySingleItem(
        JsonElement element,
        string name,
        out JsonElement item)
    {
        item = default;
        if (!TryProperty(element, name, out var array)
            || array.ValueKind != JsonValueKind.Array
            || array.GetArrayLength() != 1)
        {
            return false;
        }

        item = array[0];
        return item.ValueKind == JsonValueKind.Object;
    }
}
