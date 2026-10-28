using System.Diagnostics.Metrics;

namespace Burrfish.Infrastructure.Observability;

/// <summary>Допустимые результаты обработки событий с ограниченным набором значений.</summary>
public enum EventOutcome
{
    /// <summary>Webhook принят.</summary>
    Accepted,

    /// <summary>Webhook отклонён.</summary>
    Rejected,

    /// <summary>Новое событие сохранено.</summary>
    Stored,

    /// <summary>Повторное событие отброшено.</summary>
    Duplicate,

    /// <summary>Событие обработано.</summary>
    Processed,

    /// <summary>Событие проигнорировано как неподдерживаемое.</summary>
    Ignored
}

/// <summary>Компоненты, планирующие повторную попытку.</summary>
public enum RetryComponent
{
    /// <summary>Обработка входящего события.</summary>
    Inbox,

    /// <summary>Отправка ответа Instagram.</summary>
    Instagram,

    /// <summary>Отправка уведомления Telegram.</summary>
    Telegram
}

/// <summary>Агрегированный снимок очередей без клиентских данных и идентификаторов.</summary>
/// <param name="InstagramPending">Количество ожидающих отправок Instagram.</param>
/// <param name="InstagramFailed">Количество окончательно ошибочных отправок Instagram.</param>
/// <param name="TelegramPending">Количество ожидающих отправок Telegram.</param>
/// <param name="TelegramFailed">Количество окончательно ошибочных отправок Telegram.</param>
/// <param name="InboxOldestAgeSeconds">Возраст старейшего ожидающего входящего события в секундах.</param>
/// <param name="InstagramOldestAgeSeconds">Возраст старейшей ожидающей отправки Instagram в секундах.</param>
/// <param name="TelegramOldestAgeSeconds">Возраст старейшей ожидающей отправки Telegram в секундах.</param>
public sealed record QueueMetricsSnapshot(
    long InstagramPending,
    long InstagramFailed,
    long TelegramPending,
    long TelegramFailed,
    double InboxOldestAgeSeconds,
    double InstagramOldestAgeSeconds,
    double TelegramOldestAgeSeconds);

/// <summary>Публикует технические метрики без клиентских данных и высококардинальных тегов.</summary>
public sealed class BurrfishMetrics : IDisposable
{
    /// <summary>Стабильное имя источника метрик.</summary>
    public const string MeterName = "Burrfish";

    private readonly Meter meter = new(MeterName);
    private readonly Counter<long> events;
    private readonly Counter<long> retries;
    private readonly Counter<long> leadsCreated;
    private readonly Histogram<double> messageDuration;
    private QueueMetricsSnapshot snapshot = new(0, 0, 0, 0, 0, 0, 0);

    /// <summary>Создаёт инструменты метрик приложения.</summary>
    public BurrfishMetrics()
    {
        events = meter.CreateCounter<long>("burrfish.events", unit: "{event}");
        retries = meter.CreateCounter<long>("burrfish.retries", unit: "{retry}");
        leadsCreated = meter.CreateCounter<long>("burrfish.leads.created", unit: "{lead}");
        messageDuration = meter.CreateHistogram<double>(
            "burrfish.message.processing.duration",
            unit: "ms");
        meter.CreateObservableGauge(
            "burrfish.outbox.deliveries",
            ObserveOutbox,
            unit: "{delivery}");
        meter.CreateObservableGauge(
            "burrfish.queue.oldest.age",
            ObserveQueueAge,
            unit: "s");
    }

    /// <summary>Увеличивает счётчик событий с предопределённым результатом.</summary>
    /// <param name="outcome">Результат обработки.</param>
    public void RecordEvent(EventOutcome outcome) =>
        events.Add(1, new KeyValuePair<string, object?>("outcome", ToTag(outcome)));

    /// <summary>Увеличивает счётчик повторных попыток компонента.</summary>
    /// <param name="component">Компонент, запланировавший повтор.</param>
    public void RecordRetry(RetryComponent component) =>
        retries.Add(1, new KeyValuePair<string, object?>("component", ToTag(component)));

    /// <summary>Увеличивает число созданных заявок.</summary>
    public void RecordLeadCreated() => leadsCreated.Add(1);

    /// <summary>Записывает длительность обработки сообщения.</summary>
    /// <param name="milliseconds">Длительность в миллисекундах.</param>
    /// <param name="succeeded">Признак успешной фиксации обработки.</param>
    public void RecordMessageDuration(double milliseconds, bool succeeded) =>
        messageDuration.Record(
            milliseconds,
            new KeyValuePair<string, object?>("outcome", succeeded ? "processed" : "retry"));

    /// <summary>Заменяет агрегированный снимок очередей.</summary>
    /// <param name="value">Новый снимок.</param>
    public void UpdateQueueSnapshot(QueueMetricsSnapshot value) => Volatile.Write(ref snapshot, value);

    /// <inheritdoc />
    public void Dispose() => meter.Dispose();

    private IEnumerable<Measurement<long>> ObserveOutbox()
    {
        var value = Volatile.Read(ref snapshot);
        yield return Delivery(value.InstagramPending, "instagram", "pending");
        yield return Delivery(value.InstagramFailed, "instagram", "failed");
        yield return Delivery(value.TelegramPending, "telegram", "pending");
        yield return Delivery(value.TelegramFailed, "telegram", "failed");
    }

    private IEnumerable<Measurement<double>> ObserveQueueAge()
    {
        var value = Volatile.Read(ref snapshot);
        yield return QueueAge(value.InboxOldestAgeSeconds, "inbox");
        yield return QueueAge(value.InstagramOldestAgeSeconds, "instagram");
        yield return QueueAge(value.TelegramOldestAgeSeconds, "telegram");
    }

    private static Measurement<long> Delivery(long value, string destination, string status) =>
        new(value,
            new KeyValuePair<string, object?>("destination", destination),
            new KeyValuePair<string, object?>("status", status));

    private static Measurement<double> QueueAge(double value, string queue) =>
        new(value, new KeyValuePair<string, object?>("queue", queue));

    private static string ToTag(EventOutcome outcome) => outcome switch
    {
        EventOutcome.Accepted => "accepted",
        EventOutcome.Rejected => "rejected",
        EventOutcome.Stored => "stored",
        EventOutcome.Duplicate => "duplicate",
        EventOutcome.Processed => "processed",
        _ => "ignored"
    };

    private static string ToTag(RetryComponent component) => component switch
    {
        RetryComponent.Inbox => "inbox",
        RetryComponent.Instagram => "instagram",
        _ => "telegram"
    };
}
