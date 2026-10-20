# Instagram webhook

## Назначение

Приложение принимает webhook одного настроенного Instagram Business-аккаунта. HTTP-обработчик только проверяет запрос и надёжно сохраняет события; нормализация и бизнес-обработка выполняются позднее фоновым worker.

## Verification

`GET /webhooks/instagram` принимает параметры `hub.mode`, `hub.verify_token` и `hub.challenge`.

- `hub.mode=subscribe`, точное совпадение токена и непустой challenge возвращают `200` и challenge как `text/plain`.
- Отсутствующие, повторённые или неверные обязательные параметры возвращают `403`.

## Приём событий

`POST /webhooks/instagram` принимает не более `1 048 576` байт (`1 МиБ`).

1. Подпись `X-Hub-Signature-256` проверяется как HMAC-SHA256 от исходных байтов с ключом `Instagram:AppSecret`.
2. Тело разбирается только после успешной проверки подписи.
3. Каждый элемент `entry[].messaging[]`, содержащий `message.mid`, сохраняется отдельной inbox-записью.
4. В записи остаётся исходное событие и его `entry`-контекст, но не соседние элементы batch.
5. Ответ `200` отправляется только после завершения всех операций сохранения.

События другого аккаунта и неподдерживаемые элементы без `message` подтверждаются кодом `200` без записи. Некорректная подпись возвращает `401`, превышение лимита — `413`, некорректная структура подписанного payload — `400`.

Повторная доставка безопасна: уникальный индекс по `message.mid` и `INSERT ... ON CONFLICT DO NOTHING` не создают вторую запись. Если batch был сохранён частично из-за сбоя PostgreSQL, Meta не получает успешный ответ; при повторе уже сохранённые элементы становятся дубликатами, а остальные записываются.

## Фоновая обработка

`InboxWorker` выбирает доступные `Pending`-события в порядке `next_attempt_at`, `received_at`, `id`. PostgreSQL-запрос использует `FOR UPDATE SKIP LOCKED`, поэтому два worker не обрабатывают одну запись одновременно и могут параллельно брать разные записи.

- Поддерживаемое текстовое сообщение нормализуется и передаётся в `ProcessIncomingMessageCommand`.
- Успешный результат и прикладные изменения фиксируются атомарно со статусом `Processed`.
- Echo, вложение без текста, неизвестное или некорректное событие получает статус `Ignored` без вызова прикладного handler.
- Ожидаемая временная ошибка или исключение handler откатывает его изменения и возвращает событие в `Pending`.
- Задержка retry начинается с `30 секунд`, удваивается после каждой попытки и ограничена `900 секундами` (`15 минутами`).

Worker выключен по умолчанию через `InboxProcessing:Enabled=false`. До реализации `ProcessIncomingMessageCommandHandler` в задаче #7 сообщения продолжают накапливаться в `Pending`. Попытка включить worker без handler останавливает запуск приложения с ошибкой конфигурации.

Гарантируется однократная фиксация транзакционного результата, а не единственный запуск handler: при аварийном завершении до commit событие будет обработано повторно. Внешние действия должны создаваться через outbox внутри той же транзакции.

## Конфиденциальность

Исходное тело, подпись, `Instagram:AppSecret`, `Instagram:VerifyToken` и `Instagram:AccessToken` не должны записываться в журналы. Тестовый fixture содержит только синтетические идентификаторы и текст.

## Источники контракта

- [Meta Graph API Webhooks](https://developers.facebook.com/docs/graph-api/webhooks/)
- [Meta Instagram Platform Webhooks](https://developers.facebook.com/docs/instagram-platform/webhooks/)
- [Официальные примеры Meta Webhooks](https://github.com/fbsamples/graph-api-webhooks-samples)

Перед production-развёртыванием необходимо повторно сверить query-параметры, алгоритм подписи и форму Instagram payload с актуальной версией документации Meta.

## Проверка

```powershell
dotnet test .\tests\Burrfish.ApiContract.Tests\Burrfish.ApiContract.Tests.csproj --filter FullyQualifiedName~InstagramWebhook
dotnet test .\tests\Burrfish.IntegrationTests\Burrfish.IntegrationTests.csproj --filter FullyQualifiedName~InstagramWebhookIngestionTests
dotnet test .\tests\Burrfish.IntegrationTests\Burrfish.IntegrationTests.csproj --filter FullyQualifiedName~InboxProcessorTests
```

Вторая команда требует доступного Docker daemon.
