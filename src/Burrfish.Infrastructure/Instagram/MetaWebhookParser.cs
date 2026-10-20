using System.Text;
using System.Text.Json;
using Burrfish.Application.Instagram;
using Burrfish.Application.Persistence;
using Burrfish.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace Burrfish.Infrastructure.Instagram;

/// <summary>Выделяет события одного настроенного Instagram-аккаунта.</summary>
public sealed class MetaWebhookParser(IOptions<InstagramOptions> options)
    : IMetaWebhookParser
{
    /// <inheritdoc />
    public IReadOnlyList<InboxEnvelope>? Parse(
        ReadOnlyMemory<byte> body,
        DateTimeOffset receivedAt)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;

            if (!TryGetString(root, "object", out var objectName))
            {
                return null;
            }

            if (objectName != "instagram")
            {
                return Array.Empty<InboxEnvelope>();
            }

            if (!root.TryGetProperty("entry", out var entries)
                || entries.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var result = new List<InboxEnvelope>();
            foreach (var entry in entries.EnumerateArray())
            {
                if (!TryGetString(entry, "id", out var accountId))
                {
                    return null;
                }

                if (accountId != options.Value.AccountId)
                {
                    continue;
                }

                if (!entry.TryGetProperty("messaging", out var events))
                {
                    continue;
                }

                if (events.ValueKind != JsonValueKind.Array)
                {
                    return null;
                }

                foreach (var messagingEvent in events.EnumerateArray())
                {
                    if (messagingEvent.ValueKind != JsonValueKind.Object)
                    {
                        return null;
                    }

                    if (!messagingEvent.TryGetProperty("message", out var message))
                    {
                        continue;
                    }

                    if (!TryGetString(message, "mid", out var messageId))
                    {
                        return null;
                    }

                    result.Add(new InboxEnvelope(
                        messageId,
                        CreateSingleEventPayload(root, entry, messagingEvent),
                        receivedAt.ToUniversalTime()));
                }
            }

            return result;
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

    private static bool TryGetString(
        JsonElement element,
        string name,
        out string value)
    {
        value = string.Empty;
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty(name, out var property)
            || property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(value);
    }

    private static string CreateSingleEventPayload(
        JsonElement root,
        JsonElement entry,
        JsonElement messagingEvent)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var property in root.EnumerateObject())
            {
                if (property.Name != "entry")
                {
                    property.WriteTo(writer);
                }
            }

            writer.WriteStartArray("entry");
            writer.WriteStartObject();
            foreach (var property in entry.EnumerateObject())
            {
                if (property.Name != "messaging")
                {
                    property.WriteTo(writer);
                }
            }

            writer.WriteStartArray("messaging");
            messagingEvent.WriteTo(writer);
            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
