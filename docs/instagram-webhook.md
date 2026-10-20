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
```

Вторая команда требует доступного Docker daemon.
