# Уведомления о заявках в Telegram

## Назначение

После подтверждения передачи приложение сохраняет заявку и задание `telegram.lead-created.v1` в одной транзакции с переходом диалога в `OperatorHandoff`. `OutboxWorker` отправляет уведомление в настроенный закрытый Telegram-чат. Ошибка Telegram не удаляет заявку и не включает автоматические ответы Instagram.

## Содержимое

Уведомление содержит только:

- внутренний идентификатор заявки;
- Instagram ID клиента;
- username, если Meta его предоставила;
- подтверждённую потребность;
- момент подтверждения в UTC.

История переписки не включается. При отсутствии username сообщение явно указывает, что имя пользователя не предоставлено. Ссылка на профиль создаётся только для username длиной от `1` до `30` ASCII-символов из букв, цифр, точки и подчёркивания.

Сообщение использует Telegram HTML. Все клиентские значения экранируются. Потребность длиннее `3 000`, Instagram ID длиннее `256`, а отображаемый username длиннее `64` Unicode code points сокращаются только в уведомлении с отметкой `(сокращено)`; исходная заявка в PostgreSQL не изменяется. Эти границы вместе со служебными полями удерживают видимый текст в лимите `4 096` символов Telegram.

## Контракт Bot API

Адаптер выполняет одну попытку:

```http
POST https://api.telegram.org/bot<Telegram:BotToken>/sendMessage
Content-Type: application/json
```

```json
{
  "chat_id": -1001234567890,
  "text": "<экранированное HTML-уведомление>",
  "parse_mode": "HTML",
  "link_preview_options": {
    "is_disabled": true
  }
}
```

Успешный ответ должен содержать `ok: true` и числовой `result.message_id`. HTTP-клиент не выполняет внутренние повторы. `Telegram:TimeoutSeconds` допускает от `1` до `60 секунд`, значение по умолчанию — `15 секунд`. Ответ читается не более чем до `65 536 байт`.

## Ошибки и повторы

- `error_code = 429`, HTTP `429`, HTTP `408`, HTTP `5xx`, таймаут и сетевые ошибки планируют повтор.
- `parameters.retry_after` задаёт минимальную задержку в секундах; если она больше локального backoff, используется значение Telegram.
- Остальные корректные ошибки Bot API завершают задание как `Failed`, сохраняя исходные `error_code` и `description`.
- Отмена вызывающего кода распространяется наверх и откатывает изменение задания.
- Повреждённый payload и ссылка на отсутствующую заявку завершают конкретное задание без HTTP-вызова.

При `Sent` или `Failed` соответствующее состояние сохраняется одновременно в outbox и `LeadEntity.NotificationStatus`. При `Retry` оба объекта остаются `Pending`.

## Конфиденциальность

Токен Telegram входит в URL Bot API. Для клиента отключены стандартные HTTP-логгеры, чтобы путь запроса не попал в журнал. Текст уведомления, тело ответа и исходное описание ошибки также не журналируются. Сетевые сообщения ошибок редактируются, если содержат токен.

## Гарантии

Уникальные ключи заявки и outbox защищают от второго логического уведомления. PostgreSQL-блокировка исключает одновременную обработку одного задания двумя worker.

Если Telegram принял сообщение, а процесс остановился до фиксации `Sent`, повтор может создать второе внешнее сообщение. Bot API `sendMessage` не предоставляет используемого проектом ключа идемпотентности, поэтому ровно одна внешняя доставка не гарантируется.

## Источники

- [Telegram Bot API](https://core.telegram.org/bots/api)
- [`sendMessage`](https://core.telegram.org/bots/api/methods/sendmessage.md)
- [ResponseParameters](https://core.telegram.org/bots/api/types/responseparameters.md)
- [Formatting options](https://core.telegram.org/bots/api/formatting.md)

## Проверка

```powershell
dotnet test .\tests\Burrfish.Infrastructure.Tests\Burrfish.Infrastructure.Tests.csproj --filter FullyQualifiedName~Telegram
dotnet test .\tests\Burrfish.IntegrationTests\Burrfish.IntegrationTests.csproj --filter FullyQualifiedName~OutboxDispatcherTests
dotnet test .\tests\Burrfish.IntegrationTests\Burrfish.IntegrationTests.csproj --filter FullyQualifiedName~LeadQualificationInboxTests
```

Команды проекта `Burrfish.IntegrationTests` требуют доступного Docker daemon.
