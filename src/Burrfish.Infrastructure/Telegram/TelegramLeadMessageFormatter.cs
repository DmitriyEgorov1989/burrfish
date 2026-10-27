using System.Net;
using System.Text;
using Burrfish.Application.Telegram;

namespace Burrfish.Infrastructure.Telegram;

/// <summary>Формирует безопасное HTML-уведомление без истории переписки.</summary>
public sealed class TelegramLeadMessageFormatter
{
    private const int MaxNeedRunes = 3_000;
    private const int MaxSenderIdRunes = 256;
    private const int MaxUsernameRunes = 64;

    /// <summary>Формирует текст уведомления в режиме Telegram HTML.</summary>
    /// <param name="notification">Данные заявки; не могут быть <see langword="null"/>.</param>
    /// <returns>Экранированный текст уведомления.</returns>
    public string Format(LeadNotification notification)
    {
        var username = FormatUsername(notification.Username);
        var need = TruncateNeed(notification.Need);
        return "<b>Новая заявка</b>\n"
            + $"Заявка: <code>{Encode(notification.LeadId.ToString("D"))}</code>\n"
            + $"Instagram ID: <code>{Encode(Truncate(notification.SenderId, MaxSenderIdRunes))}</code>\n"
            + $"Профиль: {username}\n"
            + $"Потребность: {Encode(need)}\n"
            + $"Подтверждено: <code>{notification.CreatedAt.ToUniversalTime():yyyy-MM-dd HH:mm:ss 'UTC'}</code>";
    }

    private static string FormatUsername(string? username)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            return "имя пользователя не предоставлено";
        }

        var normalized = username.Trim().TrimStart('@');
        var display = $"@{Encode(Truncate(normalized, MaxUsernameRunes))}";
        return IsValidUsername(normalized)
            ? $"<a href=\"https://www.instagram.com/{normalized}/\">{display}</a>"
            : display;
    }

    private static bool IsValidUsername(string username) =>
        username is { Length: >= 1 and <= 30 }
        && username.All(character =>
            character is >= 'a' and <= 'z'
                or >= 'A' and <= 'Z'
                or >= '0' and <= '9'
                or '.'
                or '_');

    private static string TruncateNeed(string need)
    {
        return Truncate(need, MaxNeedRunes);
    }

    private static string Truncate(string value, int maxRunes)
    {
        var runes = value.EnumerateRunes().ToArray();
        if (runes.Length <= maxRunes)
        {
            return value;
        }

        var builder = new StringBuilder();
        foreach (var rune in runes.AsSpan(0, maxRunes))
        {
            builder.Append(rune.ToString());
        }
        return builder.Append("… (сокращено)").ToString();
    }

    private static string Encode(string value) => WebUtility.HtmlEncode(value);
}
