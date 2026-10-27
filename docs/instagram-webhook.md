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

В поставляемом `appsettings.json` обработка включена через `InboxProcessing:Enabled=true`. Обработчик `ProcessIncomingMessageCommandHandler` зарегистрирован через MediatR. Для приостановки обработки задайте `InboxProcessing:Enabled=false`: новые события продолжат сохраняться в `Pending`. Попытка включить обработку без зарегистрированного handler останавливает запуск приложения с ошибкой конфигурации.

Гарантируется однократная фиксация транзакционного результата, а не единственный запуск handler: при аварийном завершении до commit событие будет обработано повторно. Внешние действия должны создаваться через outbox внутри той же транзакции.

### Обработка диалога

- В `BotActive` решение `Price` или `Services` выбирает утверждённый шаблон из `DialogOptions` и добавляет `HandoffOffer`. Исходный вопрос сохраняется как ожидающая подтверждения потребность.
- `AskClarifyingQuestion` ставит в очередь проверенный вопрос ИИ, сохраняя `BotActive`.
- `ProposeHandoff` сохраняет сформулированную потребность и показывает её через `HandoffConfirmationPrompt` с подстановкой `{need}`.
- Ошибка OpenAI завершает текущее событие резервным ответом и предложением передать исходный вопрос. Ошибка интеграции не отправляется клиенту; повтор inbox ради повторного вызова OpenAI не назначается.
- В `AwaitingHandoffConfirmation` ИИ не вызывается. Полный ответ сравнивается с ограниченным набором фраз после удаления внешних пробелов, завершающих `.`, `!`, `?` и приведения регистра. Согласие: `да`, `согласен`, `согласна`, `передайте`, `да, передайте`. Отказ: `нет`, `не надо`, `не нужно`, `не передавайте`, `нет, спасибо`. Другие ответы вызывают `AmbiguousConfirmationResponse` без изменения потребности.
- Отказ возвращает `BotActive` без создания заявки и без дополнительного ответа. Согласие создаёт заявку и задание уведомления, переводит диалог в `OperatorHandoff`, не создавая заключительного ответа Instagram.
- В `OperatorHandoff` следующие сообщения отмечаются обработанными без вызова OpenAI, новых заявок и ответов.

В текущей реализации в OpenAI передаётся только текущее сообщение с пустой историей. Сохранение истории для многошагового уточнения требует отдельного этапа.

### Внутренний контракт outbox

Ответ Instagram сохраняется как `instagram.reply.v1` с ключом дедупликации `instagram.reply.v1:{ExternalEventId}` и JSON-полями `recipientId`, `text`. Уведомление сохраняется как `telegram.lead-created.v1` с ключом `telegram.lead-created.v1:{LeadId:D}` и JSON-полем `LeadId`, указывающим на сохранённую заявку.

Состояние диалога, заявка и исходящие задания фиксируются вместе со статусом inbox. При конфликте сохранения обработчик возвращает исходный технический код конфликта; `InboxProcessor` откатывает прикладные изменения и назначает повтор. Уникальные индексы защищают заявку и задания от дублирования. Обработчики фактической доставки Instagram и Telegram относятся к следующим этапам; наличие задания в outbox ещё не означает его доставку.

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
dotnet test .\tests\Burrfish.Application.Tests\Burrfish.Application.Tests.csproj --filter FullyQualifiedName~LeadQualification
dotnet test .\tests\Burrfish.IntegrationTests\Burrfish.IntegrationTests.csproj --filter FullyQualifiedName~LeadQualificationInboxTests
```

Команды проекта `Burrfish.IntegrationTests` требуют доступного Docker daemon.
