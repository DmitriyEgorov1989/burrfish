# Burrfish

Burrfish принимает сообщения Instagram Business, квалифицирует запрос через OpenAI и после согласия клиента отправляет заявку менеджеру в Telegram.

## Требования

- VPS с Docker Engine и Docker Compose;
- домен с `A`/`AAAA` записью на VPS и открытыми портами `80/tcp`, `443/tcp`, `443/udp`;
- Instagram Business-аккаунт и Meta app с разрешениями для Instagram API with Instagram Login;
- Telegram bot и идентификатор чата менеджеров;
- ключ OpenAI с доступом к настроенной модели.

Перед production-запуском проверьте актуальные требования Meta в официальной документации по [Instagram API with Instagram Login](https://developers.facebook.com/docs/instagram-platform/instagram-api-with-instagram-login/) и [Messaging API](https://developers.facebook.com/docs/instagram-platform/instagram-api-with-instagram-login/messaging-api/). Для чтения профиля и обмена сообщениями обычно требуются `instagram_business_basic` и `instagram_business_manage_messages`, но названия, уровень доступа и требования App Review могут измениться.

## Настройка

Создайте локальный `.env` на основе `.env.example` и заполните все пустые значения. `.env` исключён из Git и Docker build context. Не добавляйте токены, пароли и ключи в исходный код или документацию.

`INSTAGRAM_VERIFY_TOKEN` задаётся оператором и затем указывается в настройке Meta webhook. `TELEGRAM_CHAT_ID` должен быть числовым идентификатором чата, в который добавлен bot.

## Запуск

Проверить итоговую конфигурацию и запустить стек:

```powershell
docker compose config --quiet
docker compose build
docker compose up -d
docker compose ps
```

`config --quiet` проверяет синтаксис, не печатая подставленные секреты. Пустые обязательные значения окончательно отклоняются startup validation PostgreSQL или API.

Стек запускается в следующем порядке:

1. PostgreSQL проходит `pg_isready`.
2. Одноразовый `migrator` применяет существующие EF migrations и завершается с кодом `0`.
3. API запускает HTTP endpoints и фоновые обработчики.
4. Caddy начинает проксировать трафик после успешного `/health/ready`.

PostgreSQL и API не публикуются на интерфейсы VPS. Внешний трафик принимает только Caddy. При `DOMAIN=localhost` Caddy использует локальный сертификат; для production укажите публичный домен.

Остановить контейнеры без удаления данных:

```powershell
docker compose down
```

## Meta Webhook

В Meta app настройте callback URL:

```text
https://<DOMAIN>/webhooks/instagram
```

Укажите значение `INSTAGRAM_VERIFY_TOKEN`, подпишите приложение на события сообщений и завершите требуемые App Review и Business Verification. Проверка подписки использует `GET /webhooks/instagram`, события поступают через `POST /webhooks/instagram` с подписью `X-Hub-Signature-256`.

## Проверки Работоспособности

- `GET /health/live` подтверждает, что процесс API работает;
- `GET /health/ready` проверяет PostgreSQL и активность включённых workers;
- временная недоступность OpenAI, Instagram или Telegram не останавливает приём webhook.

Состояние контейнеров и безопасные журналы:

```powershell
docker compose ps
docker compose logs --tail 100 api
docker compose logs --tail 100 migrator
```

После настройки отправьте в Instagram вопрос «Сколько стоит?». Бот должен ответить утверждённым текстом и предложить менеджера. После ответа «Да» в Telegram должна появиться ровно одна заявка. Последующие сообщения этого клиента не должны получать автоматический ответ.

## Обновление И Migrations

Перед обновлением создайте backup, затем пересоберите образ:

```powershell
docker compose down
docker compose build --pull
docker compose up -d
```

Остановка старого API до migration исключает одновременную работу старой версии с новой схемой, но создаёт короткий перерыв в приёме webhook. Compose снова запустит одноразовый migrator до нового API. Не редактируйте применённые migrations вручную и не запускайте несколько независимых процедур обновления одновременно.

## Backup

Создать логический backup без вывода пароля или содержимого данных в журнал нужно из `cmd.exe`, а не через оператор перенаправления Windows PowerShell 5.1, который может повредить бинарный поток:

```batch
docker compose exec -T postgres pg_dump -U burrfish -d burrfish -Fc > burrfish.dump
```

Если `POSTGRES_USER` или `POSTGRES_DB` изменены, замените оба значения в команде. Храните backup за пределами VPS с ограниченным доступом. Регулярно проверяйте восстановление на отдельном изолированном PostgreSQL; восстановление в production является разрушительной операцией и должно выполняться только по утверждённой процедуре.

## Локальные Проверки

Полный integration-набор требует доступного Docker daemon:

```powershell
dotnet test .\tests\Burrfish.IntegrationTests\Burrfish.IntegrationTests.csproj --filter FullyQualifiedName~LeadFlowTests
dotnet test .\Burrfish.slnx
dotnet build .\Burrfish.slnx --no-restore
docker compose config --quiet
docker compose build
git diff --check
```
