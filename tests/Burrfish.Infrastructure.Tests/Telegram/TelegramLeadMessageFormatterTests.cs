using Burrfish.Application.Telegram;
using Burrfish.Infrastructure.Telegram;
using System.Net;
using System.Text.RegularExpressions;

namespace Burrfish.Infrastructure.Tests.Telegram;

public sealed class TelegramLeadMessageFormatterTests
{
    private static readonly DateTimeOffset CreatedAt =
        new(2026, 10, 27, 13, 14, 15, TimeSpan.Zero);

    [Fact]
    public void Message_contains_contact_need_and_utc_time_without_history()
    {
        var notification = new LeadNotification(
            Guid.Parse("11111111-2222-3333-4444-555555555555"),
            "instagram-123",
            "client.name",
            "Нужна консультация",
            CreatedAt);

        var text = new TelegramLeadMessageFormatter().Format(notification);

        Assert.Equal(
            "<b>Новая заявка</b>\n"
            + "Заявка: <code>11111111-2222-3333-4444-555555555555</code>\n"
            + "Instagram ID: <code>instagram-123</code>\n"
            + "Профиль: <a href=\"https://www.instagram.com/client.name/\">@client.name</a>\n"
            + "Потребность: Нужна консультация\n"
            + "Подтверждено: <code>2026-10-27 13:14:15 UTC</code>",
            text);
        Assert.DoesNotContain("истори", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Missing_username_is_explicit_and_user_values_are_html_encoded()
    {
        var notification = new LeadNotification(
            Guid.NewGuid(),
            "id<&>\"",
            null,
            "Нужно <важное> & срочное",
            CreatedAt);

        var text = new TelegramLeadMessageFormatter().Format(notification);

        Assert.Contains("Instagram ID: <code>id&lt;&amp;&gt;&quot;</code>", text, StringComparison.Ordinal);
        Assert.Contains("Профиль: имя пользователя не предоставлено", text, StringComparison.Ordinal);
        Assert.Contains("Потребность: Нужно &lt;важное&gt; &amp; срочное", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Unsafe_username_is_displayed_without_link()
    {
        var notification = new LeadNotification(
            Guid.NewGuid(),
            "instagram-123",
            "client/name?<script>",
            "Нужна консультация",
            CreatedAt);

        var text = new TelegramLeadMessageFormatter().Format(notification);

        Assert.Contains("Профиль: @client/name?&lt;script&gt;", text, StringComparison.Ordinal);
        Assert.DoesNotContain("href=", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Long_need_is_truncated_without_splitting_surrogate_pair()
    {
        var notification = new LeadNotification(
            Guid.NewGuid(),
            "instagram-123",
            null,
            new string('я', 3_100) + "😀",
            CreatedAt);

        var text = new TelegramLeadMessageFormatter().Format(notification);

        Assert.Contains("… (сокращено)", text, StringComparison.Ordinal);
        Assert.DoesNotContain("�", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Entire_visible_message_stays_within_telegram_limit()
    {
        var notification = new LeadNotification(
            Guid.NewGuid(),
            new string('&', 2_000),
            new string('x', 2_000),
            new string('я', 4_000),
            CreatedAt);

        var html = new TelegramLeadMessageFormatter().Format(notification);
        var visible = WebUtility.HtmlDecode(Regex.Replace(html, "<[^>]+>", string.Empty));

        Assert.True(visible.Length <= 4_096, $"Visible length was {visible.Length}.");
    }
}
