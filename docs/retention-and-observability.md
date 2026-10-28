# Хранение данных и наблюдаемость

## Срок хранения

`DataRetentionWorker` запускает очистку сразу после старта приложения и затем через интервал `DataRetention:IntervalMinutes`. Production-значение интервала — 60 минут.

Граница вычисляется один раз на запуск очистки как текущее UTC-время минус `Dialog:RetentionDays`. Значение срока фиксировано в 30 календарных дней. Записи на границе и старше неё удаляются из:

- `inbox_events` по `received_at`, включая исходный webhook payload;
- `outbox_messages` по `created_at`, независимо от статуса доставки;
- `conversations` по `created_at`;
- `leads` по `created_at`.

Удаление выполняется одной транзакцией PostgreSQL. Для неотправленных outbox-заданий журналируется только технический код `RetentionExpired` и агрегированное количество, без payload или идентификаторов. После удаления `OperatorHandoff` следующее входящее сообщение создаёт новый диалог в `BotActive`.

## Проверки состояния

- `GET /health/live` возвращает HTTP `200`, если HTTP-процесс способен обработать запрос.
- `GET /health/ready` возвращает HTTP `200`, если PostgreSQL доступен и сигналы активности всех включённых workers свежие; иначе возвращает HTTP `503`.

Проверки Meta, OpenAI, Instagram Graph API и Telegram не входят в readiness. Их временная недоступность обрабатывается через retry и не останавливает приём webhook. Ответ health endpoint содержит только итоговый статус и не содержит строку подключения, exception, stack trace или сведения внешних API.

Для inbox, outbox и обновления метрик сигнал считается свежим в течение не менее 120 секунд либо трёх рабочих интервалов, если они дольше. Для retention допустимый возраст равен двум интервалам очистки.

## Метрики

Приложение публикует инструменты `System.Diagnostics.Metrics` через meter `Burrfish`:

| Инструмент | Тип | Единица | Теги |
|---|---|---|---|
| `burrfish.events` | counter | `{event}` | `outcome`: `accepted`, `rejected`, `stored`, `duplicate`, `processed`, `ignored` |
| `burrfish.retries` | counter | `{retry}` | `component`: `inbox`, `instagram`, `telegram` |
| `burrfish.leads.created` | counter | `{lead}` | нет |
| `burrfish.message.processing.duration` | histogram | `ms` | `outcome`: `processed`, `retry` |
| `burrfish.outbox.deliveries` | observable gauge | `{delivery}` | `destination`, `status` |
| `burrfish.queue.oldest.age` | observable gauge | `s` | `queue`: `inbox`, `instagram`, `telegram` |

Снимок очередей обновляется каждые `Observability:QueueRefreshIntervalSeconds`; production-значение — 5 секунд. Метрики не используют тексты, внешние идентификаторы, имена пользователей, токены, URL или произвольные коды внешних ошибок как значения тегов.

Текущая версия создаёт стандартные .NET-инструменты, но не добавляет публичный endpoint экспорта. Подключение конкретного exporter относится к конфигурации развёртывания.

## Безопасные журналы

В журналы разрешено записывать тип компонента, ограниченный технический результат, длительность, число попыток и агрегированное количество. Запрещены:

- полный webhook payload и текст сообщения;
- потребность заявки, username и Instagram ID клиента;
- access token, bot token, API key, verify token и строка подключения;
- произвольный текст исключения внешней системы.

EF Core sensitive data logging отключён. Заголовки OpenAI и Instagram редактируются, а стандартные HTTP-логгеры Telegram отключены, поскольку bot token является частью URL.
