# Доставка ответов Instagram

## Назначение

Ответы бота сохраняются как задания `instagram.reply.v1` в PostgreSQL outbox. `OutboxWorker` выбирает по одному доступному поддерживаемому заданию; Instagram-ответы он отправляет через Instagram API with Instagram Login. Доставка `telegram.lead-created.v1` описана в `docs/telegram-notifications.md`.

## Контракт Meta

Адаптер выполняет один запрос:

```http
POST https://graph.instagram.com/{Instagram:ApiVersion}/{Instagram:AccountId}/messages
Authorization: Bearer <Instagram:AccessToken>
Content-Type: application/json
```

```json
{
  "recipient": {
    "id": "<recipientId>"
  },
  "message": {
    "text": "<text>"
  }
}
```

Успешный ответ должен содержать непустой `message_id`. В поставляемой конфигурации закреплена версия `v26.0`; перед production-развёртыванием её поддержку и требуемые разрешения необходимо повторно сверить по актуальной официальной документации Meta.

HTTP-клиент не выполняет внутренние повторы. Таймаут одной попытки задаётся `Instagram:SendTimeoutSeconds`, допустимый диапазон — от `1` до `60 секунд`, значение по умолчанию — `15 секунд`. Ответ Meta читается не более чем до `65 536 байт`.

## Состояния и повторы

`OutboxDispatcher` выбирает доступные `Pending`-задания поддерживаемых типов через `FOR UPDATE SKIP LOCKED`. Правила ниже относятся к `instagram.reply.v1`.

- Успешная отправка переводит задание в `Sent`; повторно оно не выбирается.
- HTTP `408`, HTTP `429`, HTTP `5xx`, сетевая ошибка, таймаут и `error.is_transient=true` возвращают задание в `Pending`.
- Прочие HTTP `4xx` переводят задание в `Failed` с исходными `error.code` и `error.message`.
- Некорректный JSON задания переводит его в `Failed` без обращения к Meta.
- Отмена вызывающего кода распространяется наверх и откатывает изменение задания.

Расчётная задержка повтора начинается с `30 секунд`, удваивается до `900 секунд` (`15 минут`). Последовательность: `30`, `60`, `120`, `240`, `480`, `900 секунд`. Если корректный `Retry-After` требует ждать дольше, используется указанная Meta задержка.

Настройки worker находятся в секции `OutboxProcessing`: `Enabled`, `PollIntervalMilliseconds`, `RetryBaseDelaySeconds`, `MaxRetryDelaySeconds`.

## Гарантии

Уникальный `DeduplicationKey` гарантирует одно сохраняемое задание на один логический ответ. Блокировка строки исключает одновременную отправку одного задания двумя worker.

Ровно одна внешняя доставка не гарантируется: если Meta принял сообщение, а процесс остановился до фиксации `Sent`, следующая попытка может отправить его повторно. Устранить этот промежуток можно только при наличии подтверждённого механизма идемпотентности в контракте Meta.

## Конфиденциальность

Токен передаётся только в заголовке `Authorization`. Значения заголовков, текст сообщения, тело ответа и исходное сообщение ошибки не записываются в журнал. В базе исходные код и сообщение ошибки сохраняются для диагностики, но не отправляются клиенту.

## Источники контракта

- [Instagram API with Instagram Login — Messaging API](https://developers.facebook.com/docs/instagram-platform/instagram-api-with-instagram-login/messaging-api)
- [Meta Instagram workspace в Postman](https://www.postman.com/meta/instagram/overview)

## Проверка

```powershell
dotnet test .\tests\Burrfish.Infrastructure.Tests\Burrfish.Infrastructure.Tests.csproj --filter FullyQualifiedName~InstagramGraphApi
dotnet test .\tests\Burrfish.Infrastructure.Tests\Burrfish.Infrastructure.Tests.csproj --filter FullyQualifiedName~OutboxWorkerTests
dotnet test .\tests\Burrfish.IntegrationTests\Burrfish.IntegrationTests.csproj --filter FullyQualifiedName~OutboxDispatcherTests
```

Последняя команда требует доступного Docker daemon.
