# Instagram Lead Bot Implementation Plan

> **Для агентных исполнителей:** выполняйте задачи последовательно по этому документу. Недоступные `subagent-driven-development` и `executing-plans` не устанавливайте автоматически. Шаги отмечаются флажками `- [ ]`.

**Цель:** создать надёжного русскоязычного бота для одного Instagram Business-аккаунта, который отвечает утверждёнными шаблонами, уточняет потребность через OpenAI и после подтверждения уведомляет менеджера в Telegram.

**Архитектура:** один ASP.NET Core модульный монолит с Clean Architecture, DDD и CQRS через MediatR. `Domain` защищает инварианты без внешних зависимостей, `Application` содержит команды, запросы и порты, `Infrastructure` реализует persistence и внешние адаптеры, `Api` служит composition root. Полные правила находятся в `docs/architecture.md`.

**Стек:** .NET 10, ASP.NET Core, MediatR 12.5.0, EF Core, Npgsql, PostgreSQL, xUnit, Testcontainers for .NET, OpenAI HTTP API, Meta Instagram Messaging API, Telegram Bot API, Docker Compose.

**Спецификация:** `docs/superpowers/specs/2026-10-14-instagram-lead-bot-design.md`

## Общие ограничения

- Один Instagram Business-аккаунт и русский язык.
- Намерения первой версии: `price`, `services`, `unknown`.
- OpenAI не сообщает факты о компании и не создаёт заявку.
- Заявка создаётся только после явного подтверждения клиента.
- После передачи менеджеру бот молчит 30 календарных дней.
- Тексты сообщений и заявки удаляются через 30 календарных дней.
- Секреты поступают только из переменных окружения или Docker secrets.
- Полные тексты сообщений не записываются в журналы.
- Граф зависимостей проектов и правила DDD/CQRS из `docs/architecture.md` обязательны для каждой задачи.
- `Burrfish.Domain` не зависит от MediatR, EF Core, ASP.NET Core или внешних API.
- Команды изменяют состояние, запросы не изменяют состояние, endpoints и workers используют `ISender`.
- Используется `MediatR 12.5.0` с лицензией Apache-2.0; смена лицензии или версии требует согласования.
- Сначала выполняется наиболее узкий тест, затем тесты проекта, сборка и `git diff --check`.
- Перед добавлением NuGet-зависимостей исполнитель согласует их точные версии.
- Перед Task 3 требуется разрешение на создание EF migration.
- Перед Task 4 требуется разрешение на введение публичного HTTP-контракта webhook.
- Коммиты, push и закрытие issues выполняются только после разрешения по `AGENTS.md`.

## Структура файлов

```text
Burrfish.slnx
Directory.Build.props
Directory.Packages.props
src/
  Burrfish.Domain/
  Burrfish.Application/
  Burrfish.Infrastructure/
  Burrfish.Api/
    Program.cs
    appsettings.json
    Properties/launchSettings.json
tests/
  Burrfish.Domain.Tests/
  Burrfish.Application.Tests/
  Burrfish.Infrastructure.Tests/
  Burrfish.IntegrationTests/
  Burrfish.ApiContract.Tests/
deploy/
  Caddyfile
docker-compose.yml
Dockerfile
README.md
```

`Burrfish.Api` остаётся единственным исполняемым проектом и собирает `Application` и `Infrastructure` в один процесс. Разрешённые ссылки проектов определены в `docs/architecture.md` и проверяются архитектурными тестами.

---

### Task 1: Создать запускаемый каркас и типизированную конфигурацию

**Результат:** Clean Architecture solution запускается на .NET 10, регистрирует MediatR 12.5.0, валидирует обязательные настройки при старте и не содержит секретов в репозитории.

**Файлы:**
- Create: `Burrfish.slnx`
- Create: `Directory.Build.props`
- Create: `Directory.Packages.props`
- Create: `src/Burrfish.Domain/Burrfish.Domain.csproj`
- Create: `src/Burrfish.Application/Burrfish.Application.csproj`
- Create: `src/Burrfish.Application/DependencyInjection.cs`
- Create: `src/Burrfish.Infrastructure/Burrfish.Infrastructure.csproj`
- Create: `src/Burrfish.Infrastructure/DependencyInjection.cs`
- Create: `src/Burrfish.Infrastructure/Configuration/InstagramOptions.cs`
- Create: `src/Burrfish.Infrastructure/Configuration/OpenAiOptions.cs`
- Create: `src/Burrfish.Infrastructure/Configuration/TelegramOptions.cs`
- Create: `src/Burrfish.Infrastructure/Configuration/DialogOptions.cs`
- Create: `src/Burrfish.Api/Burrfish.Api.csproj`
- Create: `src/Burrfish.Api/Program.cs`
- Create: `src/Burrfish.Api/appsettings.json`
- Create: `tests/Burrfish.Domain.Tests/Burrfish.Domain.Tests.csproj`
- Create: `tests/Burrfish.Application.Tests/Burrfish.Application.Tests.csproj`
- Create: `tests/Burrfish.Infrastructure.Tests/Burrfish.Infrastructure.Tests.csproj`
- Create: `tests/Burrfish.Infrastructure.Tests/Configuration/OptionsValidationTests.cs`
- Create: `tests/Burrfish.IntegrationTests/Burrfish.IntegrationTests.csproj`
- Create: `tests/Burrfish.ApiContract.Tests/Burrfish.ApiContract.Tests.csproj`

**Интерфейсы:**
- Consumes: отсутствуют.
- Produces: однонаправленный граф проектов, регистрацию MediatR, `InstagramOptions`, `OpenAiOptions`, `TelegramOptions`, `DialogOptions`; запускаемый host и тестовые проекты по архитектурным границам.

- [x] **Step 1: создать solution и проекты**

```powershell
dotnet new sln -n Burrfish --format slnx
dotnet new classlib -n Burrfish.Domain -o .\src\Burrfish.Domain --framework net10.0
dotnet new classlib -n Burrfish.Application -o .\src\Burrfish.Application --framework net10.0
dotnet new classlib -n Burrfish.Infrastructure -o .\src\Burrfish.Infrastructure --framework net10.0
dotnet new web -n Burrfish.Api -o .\src\Burrfish.Api --framework net10.0
dotnet new xunit -n Burrfish.Domain.Tests -o .\tests\Burrfish.Domain.Tests --framework net10.0
dotnet new xunit -n Burrfish.Application.Tests -o .\tests\Burrfish.Application.Tests --framework net10.0
dotnet new xunit -n Burrfish.Infrastructure.Tests -o .\tests\Burrfish.Infrastructure.Tests --framework net10.0
dotnet new xunit -n Burrfish.IntegrationTests -o .\tests\Burrfish.IntegrationTests --framework net10.0
dotnet new xunit -n Burrfish.ApiContract.Tests -o .\tests\Burrfish.ApiContract.Tests --framework net10.0
dotnet sln .\Burrfish.slnx add .\src .\tests
```

- [x] **Step 2: написать падающие тесты валидации**

Проверить отсутствие `InstagramOptions.AppSecret`, пустой `OpenAiOptions.ApiKey`, неположительный `DialogOptions.RetentionDays` и отсутствие `TelegramOptions.ChatId`. Ожидать ошибку валидации с именем конкретного свойства без вывода его значения.

- [x] **Step 3: запустить узкий тест и подтвердить падение**

```powershell
dotnet test .\tests\Burrfish.Infrastructure.Tests\Burrfish.Infrastructure.Tests.csproj --filter FullyQualifiedName~OptionsValidationTests
```

Ожидается `FAIL`, поскольку options и validators ещё отсутствуют.

- [x] **Step 4: реализовать options и регистрацию**

Пример контракта:

```csharp
namespace Burrfish.Infrastructure.Configuration;

/// <summary>Настройки подключения одного Instagram Business-аккаунта.</summary>
public sealed class InstagramOptions
{
    /// <summary>Имя секции конфигурации.</summary>
    public const string SectionName = "Instagram";

    /// <summary>Идентификатор Instagram-аккаунта; не может быть <see langword="null"/>.</summary>
    public string AccountId { get; init; } = string.Empty;

    /// <summary>Секрет Meta-приложения; не может быть <see langword="null"/>.</summary>
    public string AppSecret { get; init; } = string.Empty;

    /// <summary>Токен проверки webhook; не может быть <see langword="null"/>.</summary>
    public string VerifyToken { get; init; } = string.Empty;

    /// <summary>Токен отправки сообщений; не может быть <see langword="null"/>.</summary>
    public string AccessToken { get; init; } = string.Empty;
}
```

Использовать `ValidateOnStart`; в `appsettings.json` оставить только пустые несекретные ключи и шаблоны `price`, `services`, fallback и confirmation.

- [x] **Step 5: пройти тесты и сборку**

```powershell
dotnet test .\tests\Burrfish.Infrastructure.Tests\Burrfish.Infrastructure.Tests.csproj --filter FullyQualifiedName~OptionsValidationTests
dotnet build .\Burrfish.slnx
git diff --check
```

Ожидается `PASS` и успешная сборка.

---

### Task 2: Реализовать модель состояния диалога

**Результат:** чистая доменная модель запрещает заявку без подтверждения и любые ответы после передачи менеджеру.

**Файлы:**
- Create: `src/Burrfish.Domain/Conversations/ConversationState.cs`
- Create: `src/Burrfish.Domain/Conversations/Conversation.cs`
- Create: `src/Burrfish.Domain/Conversations/LeadDraft.cs`
- Create: `src/Burrfish.Domain/Conversations/ConversationTransitionResult.cs`
- Create: `src/Burrfish.Domain/Conversations/HandoffConfirmationResult.cs`
- Create: `tests/Burrfish.Domain.Tests/Conversations/ConversationTests.cs`

**Интерфейсы:**
- Consumes: системный `DateTimeOffset` с нормализацией времени подтверждения в UTC.
- Produces: `Conversation.Start()`, `Conversation.ProposeHandoff(string?)`, `Conversation.ConfirmHandoff(DateTimeOffset)`, `Conversation.DeclineHandoff()`, `Conversation.CanReply`; `LeadDraft.Need`, `LeadDraft.CreatedAt`; `ConversationTransitionResult`, `HandoffConfirmationResult`.

- [x] **Step 1: написать тесты переходов**

Покрыть: новое состояние `BotActive`; предложение передачи сохраняет непустую потребность и переводит в `AwaitingHandoffConfirmation`; подтверждение возвращает `LeadDraft` и переводит в `OperatorHandoff`; отказ возвращает `BotActive`; подтверждение из неверного состояния является ожидаемой доменной ошибкой без исключения; `CanReply == false` после передачи.

- [x] **Step 2: запустить узкий тест**

```powershell
dotnet test .\tests\Burrfish.Domain.Tests\Burrfish.Domain.Tests.csproj --filter FullyQualifiedName~ConversationTests
```

Ожидается `FAIL` из-за отсутствующих типов.

- [x] **Step 3: реализовать минимальную модель**

```csharp
namespace Burrfish.Domain.Conversations;

/// <summary>Состояние автоматической обработки диалога.</summary>
public enum ConversationState
{
    BotActive,
    AwaitingHandoffConfirmation,
    OperatorHandoff
}

/// <summary>Результат ожидаемого перехода состояния без исключений управления потоком.</summary>
/// <param name="Succeeded">Признак выполненного перехода.</param>
/// <param name="ErrorCode">Код ожидаемой ошибки; <see langword="null"/>, если переход выполнен.</param>
public sealed record ConversationTransitionResult(bool Succeeded, string? ErrorCode);

/// <summary>Результат подтверждения передачи диалога менеджеру.</summary>
/// <param name="Succeeded">Признак выполненной передачи.</param>
/// <param name="ErrorCode">Код ожидаемой ошибки; <see langword="null"/> при успехе.</param>
/// <param name="Lead">Черновик заявки; <see langword="null"/>, если переход отклонён.</param>
public sealed record HandoffConfirmationResult(
    bool Succeeded,
    string? ErrorCode,
    LeadDraft? Lead);
```

`ConfirmHandoff` возвращает `HandoffConfirmationResult`; `Lead` равен `null`, если переход отклонён. `LeadDraft` содержит подтверждённую потребность `Need` и нормализованный момент подтверждения `CreatedAt` в UTC.

- [x] **Step 4: пройти тест и все unit-тесты**

```powershell
dotnet test .\tests\Burrfish.Domain.Tests\Burrfish.Domain.Tests.csproj --filter FullyQualifiedName~ConversationTests
dotnet test .\tests\Burrfish.Domain.Tests\Burrfish.Domain.Tests.csproj
```

---

### Task 3: Добавить PostgreSQL, inbox/outbox и дедупликацию

**Согласовано:** первая EF migration; `Microsoft.EntityFrameworkCore 10.0.13`, `Npgsql.EntityFrameworkCore.PostgreSQL 10.0.3`, `Npgsql 10.0.3`, `Testcontainers.PostgreSql 4.16.0` и локальный `dotnet-ef 10.0.13`; применение migration разрешено только к изолированному тестовому PostgreSQL-контейнеру.

**Результат:** входящее событие сохраняется один раз, а изменение диалога и исходящее задание могут фиксироваться атомарно.

**Файлы:**
- Create: `src/Burrfish.Application/Persistence/InboxEnvelope.cs`
- Create: `src/Burrfish.Application/Persistence/IInboxWriter.cs`
- Create: `src/Burrfish.Application/Persistence/StoredConversation.cs`
- Create: `src/Burrfish.Application/Persistence/IConversationRepository.cs`
- Create: `src/Burrfish.Application/Persistence/OutboxEnvelope.cs`
- Create: `src/Burrfish.Application/Persistence/IOutboxWriter.cs`
- Create: `src/Burrfish.Application/Persistence/ILeadRepository.cs`
- Create: `src/Burrfish.Application/Persistence/IConversationPersistence.cs`
- Create: `src/Burrfish.Application/Persistence/PersistenceCommitResult.cs`
- Create: `src/Burrfish.Infrastructure/Configuration/DatabaseOptions.cs`
- Create: `src/Burrfish.Infrastructure/Persistence/BurrfishDbContext.cs`
- Create: `src/Burrfish.Infrastructure/Persistence/BurrfishDbContextFactory.cs`
- Create: `src/Burrfish.Infrastructure/Persistence/Entities/InboxEvent.cs`
- Create: `src/Burrfish.Infrastructure/Persistence/Entities/OutboxMessage.cs`
- Create: `src/Burrfish.Infrastructure/Persistence/Entities/ConversationEntity.cs`
- Create: `src/Burrfish.Infrastructure/Persistence/Entities/LeadEntity.cs`
- Create: `src/Burrfish.Infrastructure/Persistence/InboxWriter.cs`
- Create: `src/Burrfish.Infrastructure/Persistence/ConversationRepository.cs`
- Create: `src/Burrfish.Infrastructure/Persistence/LeadRepository.cs`
- Create: `src/Burrfish.Infrastructure/Persistence/OutboxWriter.cs`
- Create: `src/Burrfish.Infrastructure/Persistence/ConversationPersistence.cs`
- Create: `src/Burrfish.Infrastructure/Persistence/Migrations/20261016102146_Initial.cs`
- Create: `tests/Burrfish.IntegrationTests/Persistence/PostgresFixture.cs`
- Create: `tests/Burrfish.IntegrationTests/Persistence/MigrationTests.cs`
- Create: `tests/Burrfish.IntegrationTests/Persistence/InboxWriterTests.cs`
- Create: `tests/Burrfish.IntegrationTests/Persistence/ConversationRepositoryTests.cs`
- Create: `tests/Burrfish.IntegrationTests/Persistence/AtomicPersistenceTests.cs`
- Create: `tests/Burrfish.IntegrationTests/Persistence/PersistenceConcurrencyTests.cs`

**Интерфейсы:**
- Consumes: `Conversation`, `ConversationState`, `LeadDraft` из Task 2; `Database__ConnectionString` из среды выполнения.
- Produces: `IInboxWriter.TryStoreAsync(InboxEnvelope, CancellationToken)`, `IConversationRepository.FindAsync`, `IConversationRepository.Add`, `IOutboxWriter.Add`, `ILeadRepository.Add`, `IConversationPersistence.CommitAsync`, `BurrfishDbContext`, таблицы `inbox_events`, `outbox_messages`, `conversations`, `leads`.

- [x] **Step 1: определить входной контракт**

```csharp
/// <summary>Событие внешней платформы до бизнес-обработки.</summary>
/// <param name="ExternalEventId">Уникальный идентификатор Meta; не может быть <see langword="null"/>.</param>
/// <param name="Payload">Исходный JSON для отложенной нормализации; не может быть <see langword="null"/>.</param>
/// <param name="ReceivedAt">Момент получения в UTC.</param>
public sealed record InboxEnvelope(string ExternalEventId, string Payload, DateTimeOffset ReceivedAt);

/// <summary>Сохраняет входящие события с дедупликацией.</summary>
public interface IInboxWriter
{
    /// <summary>Возвращает <see langword="true"/> только для впервые сохранённого события.</summary>
    Task<bool> TryStoreAsync(InboxEnvelope envelope, CancellationToken cancellationToken);
}
```

- [x] **Step 2: написать интеграционные тесты**

Первый insert одного `ExternalEventId` возвращает `true`; второй возвращает `false`; восемь параллельных inserts оставляют одну строку; rollback не сохраняет ни заявку, ни outbox; commit сохраняет заявку, `OperatorHandoff` и Telegram outbox вместе. Отдельные тесты проверяют roundtrip агрегата, unique conflict и `xmin` concurrency conflict.

- [x] **Step 3: запустить узкий тест с Docker**

```powershell
dotnet test .\tests\Burrfish.IntegrationTests\Burrfish.IntegrationTests.csproj --filter FullyQualifiedName~InboxWriterTests
```

Ожидается `FAIL`; для выполнения нужен доступный Docker daemon.

- [x] **Step 4: реализовать schema и migration**

Установлены unique index на `inbox_events.external_event_id`, индексы pending/status для inbox/outbox и `xmin` concurrency token на изменяемых записях. `Conversation` отображается как обязательный owned-агрегат, поэтому его private setters сохраняются без обхода доменных переходов. Payload и тексты сообщений не выводятся в лог при конфликте.

- [x] **Step 5: пройти узкие и проектные тесты**

```powershell
dotnet test .\tests\Burrfish.IntegrationTests\Burrfish.IntegrationTests.csproj --filter FullyQualifiedName~Persistence
dotnet test .\Burrfish.slnx
```

---

### Task 4: Принять и проверить Meta webhook

**Перед началом:** получить разрешение на публичный HTTP-контракт `GET /webhooks/instagram` и `POST /webhooks/instagram`; сверить payload и подпись с актуальной официальной документацией Meta.

**Результат:** приложение проходит webhook verification, отклоняет неверную подпись и надёжно сохраняет поддерживаемые события до ответа `200`.

**Файлы:**
- Create: `src/Burrfish.Api/Endpoints/InstagramWebhookEndpoints.cs`
- Create: `src/Burrfish.Infrastructure/Instagram/MetaSignatureVerifier.cs`
- Create: `src/Burrfish.Infrastructure/Instagram/MetaEventIdentity.cs`
- Create: `tests/Burrfish.ApiContract.Tests/Instagram/Fixtures/text-message.json`
- Create: `tests/Burrfish.ApiContract.Tests/Instagram/InstagramWebhookVerificationTests.cs`
- Create: `tests/Burrfish.ApiContract.Tests/Instagram/InstagramWebhookSignatureTests.cs`
- Create: `tests/Burrfish.ApiContract.Tests/Instagram/InstagramWebhookPersistenceTests.cs`

**Интерфейсы:**
- Consumes: `InstagramOptions`, `IInboxWriter`.
- Produces: публичные webhook endpoints и `IMetaSignatureVerifier.IsValid(ReadOnlySpan<byte>, string?)`.

- [x] **Step 1: сохранить обезличенный fixture и написать контрактные тесты**

Проверить корректный challenge при совпадении verify token; `403` при несовпадении; `401` при отсутствующей/неверной подписи POST; `200` после сохранения валидного события; повторный webhook также получает `200`, но не создаёт вторую запись.

- [x] **Step 2: запустить самый узкий тест**

```powershell
dotnet test .\tests\Burrfish.ApiContract.Tests\Burrfish.ApiContract.Tests.csproj --filter FullyQualifiedName~InstagramWebhookVerificationTests
```

- [x] **Step 3: реализовать endpoint без ожидания бизнес-обработки**

Пример регистрации:

```csharp
app.MapGet("/webhooks/instagram", InstagramWebhookEndpoints.Verify);
app.MapPost("/webhooks/instagram", InstagramWebhookEndpoints.Receive);
```

Подпись вычислять по исходным байтам body через HMAC и сравнивать constant-time. Не десериализовать неподтверждённый body и не журналировать его.

- [x] **Step 4: пройти контрактные тесты**

```powershell
dotnet test .\tests\Burrfish.ApiContract.Tests\Burrfish.ApiContract.Tests.csproj --filter FullyQualifiedName~InstagramWebhook
dotnet test .\tests\Burrfish.ApiContract.Tests\Burrfish.ApiContract.Tests.csproj
```

---

### Task 5: Нормализовать и обрабатывать inbox в фоне

**Результат:** поддерживаемое текстовое событие превращается в внутреннее сообщение и обрабатывается один раз; неподдерживаемые события завершаются без ответа.

**Файлы:**
- Create: `src/Burrfish.Application/Instagram/IncomingInstagramMessage.cs`
- Create: `src/Burrfish.Application/Instagram/ProcessIncomingMessageCommand.cs`
- Create: `src/Burrfish.Infrastructure/Instagram/InstagramEventNormalizer.cs`
- Create: `src/Burrfish.Infrastructure/Processing/InboxProcessor.cs`
- Create: `src/Burrfish.Infrastructure/Processing/InboxWorker.cs`
- Create: `tests/Burrfish.Infrastructure.Tests/Instagram/InstagramEventNormalizerTests.cs`
- Create: `tests/Burrfish.IntegrationTests/Processing/InboxProcessorTests.cs`

**Интерфейсы:**
- Consumes: pending `InboxEvent`, fixture из Task 4.
- Produces: `IncomingInstagramMessage? Normalize(string payload)` и `ProcessIncomingMessageCommand`, отправляемую worker через `ISender`.

- [ ] **Step 1: написать тесты normalizer**

```csharp
/// <summary>Нормализованное текстовое сообщение Instagram.</summary>
/// <param name="ExternalEventId">Идентификатор события; не может быть <see langword="null"/>.</param>
/// <param name="ConversationId">Идентификатор диалога; не может быть <see langword="null"/>.</param>
/// <param name="SenderId">Instagram ID клиента; не может быть <see langword="null"/>.</param>
/// <param name="Username">Доступное имя пользователя; <see langword="null"/>, если Meta его не передала.</param>
/// <param name="Text">Текст сообщения; не может быть <see langword="null"/>.</param>
/// <param name="ReceivedAt">Момент сообщения в UTC.</param>
public sealed record IncomingInstagramMessage(
    string ExternalEventId,
    string ConversationId,
    string SenderId,
    string? Username,
    string Text,
    DateTimeOffset ReceivedAt);
```

Проверить текст, отсутствующий username, echo/исходящее сообщение, вложение без текста, неизвестный event type и malformed JSON.

- [ ] **Step 2: запустить узкий unit-тест и реализовать normalizer**

```powershell
dotnet test .\tests\Burrfish.Infrastructure.Tests\Burrfish.Infrastructure.Tests.csproj --filter FullyQualifiedName~InstagramEventNormalizerTests
```

- [ ] **Step 3: написать и реализовать интеграционный цикл worker**

Worker атомарно забирает доступное событие, отправляет `ProcessIncomingMessageCommand` через `ISender`, отмечает `Processed`; ожидаемая ошибка normalizer отмечает `Ignored`; временная ошибка оставляет событие для retry с `NextAttemptAt`.

- [ ] **Step 4: проверить отсутствие двойной обработки**

```powershell
dotnet test .\tests\Burrfish.IntegrationTests\Burrfish.IntegrationTests.csproj --filter FullyQualifiedName~InboxProcessorTests
```

---

### Task 6: Добавить ограниченный OpenAI-клиент

**Результат:** OpenAI возвращает только валидированное структурированное решение: известное намерение, уточняющий вопрос или сформулированную потребность.

**Файлы:**
- Create: `src/Burrfish.Application/OpenAI/AiDialogDecision.cs`
- Create: `src/Burrfish.Application/OpenAI/AiDialogRequest.cs`
- Create: `src/Burrfish.Application/OpenAI/IAiDialogClient.cs`
- Create: `src/Burrfish.Infrastructure/OpenAI/OpenAiDialogClient.cs`
- Create: `src/Burrfish.Infrastructure/OpenAI/OpenAiResponseValidator.cs`
- Create: `tests/Burrfish.Infrastructure.Tests/OpenAI/OpenAiResponseValidatorTests.cs`
- Create: `tests/Burrfish.Infrastructure.Tests/OpenAI/OpenAiDialogClientTests.cs`

**Интерфейсы:**
- Consumes: `OpenAiOptions`, текст и ограниченную историю диалога.
- Produces: `IAiDialogClient.DecideAsync(AiDialogRequest, CancellationToken)` и закрытый набор `AiDialogDecisionKind`.

- [ ] **Step 1: определить закрытый контракт решения**

```csharp
/// <summary>Допустимое решение ИИ; произвольное действие не поддерживается.</summary>
public enum AiDialogDecisionKind
{
    Price,
    Services,
    AskClarifyingQuestion,
    ProposeHandoff
}

/// <summary>Валидированное решение ИИ.</summary>
/// <param name="Kind">Тип разрешённого действия.</param>
/// <param name="Text">Уточняющий вопрос или потребность; <see langword="null"/> для шаблонных намерений.</param>
public sealed record AiDialogDecision(AiDialogDecisionKind Kind, string? Text);
```

- [ ] **Step 2: написать validator-тесты**

Проверить неизвестный kind, пустой вопрос, пустую потребность, лишнее действие, слишком длинный текст и корректные `price`/`services`. Некорректный ответ возвращает ожидаемый failure с исходным кодом/сообщением интеграции, но без полного model output в логе.

- [ ] **Step 3: запустить узкий тест и реализовать validator**

```powershell
dotnet test .\tests\Burrfish.Infrastructure.Tests\Burrfish.Infrastructure.Tests.csproj --filter FullyQualifiedName~OpenAiResponseValidatorTests
```

- [ ] **Step 4: реализовать HTTP-клиент и контрактные тесты через подменный handler**

System prompt отделяет инструкции от недоверенного текста клиента. Запрос требует JSON schema; timeout и transient HTTP errors возвращаются как интеграционный failure, а не исключение бизнес-потока.

- [ ] **Step 5: пройти OpenAI-тесты**

```powershell
dotnet test .\tests\Burrfish.Infrastructure.Tests\Burrfish.Infrastructure.Tests.csproj --filter FullyQualifiedName~OpenAiDialogClientTests
dotnet test .\tests\Burrfish.Infrastructure.Tests\Burrfish.Infrastructure.Tests.csproj
```

---

### Task 7: Оркестрировать шаблоны, уточнение и подтверждение заявки

**Результат:** входящее сообщение приводит к одному детерминированному исходу: шаблон, уточнение, предложение передачи, подтверждение, отказ или отсутствие ответа.

**Файлы:**
- Create: `src/Burrfish.Application/LeadQualification/ProcessIncomingMessageCommandHandler.cs`
- Create: `src/Burrfish.Application/LeadQualification/ConfirmationClassifier.cs`
- Create: `src/Burrfish.Application/LeadQualification/DialogReply.cs`
- Create: `src/Burrfish.Application/LeadQualification/DialogTemplates.cs`
- Create: `src/Burrfish.Application/LeadQualification/IDialogTemplateProvider.cs`
- Create: `src/Burrfish.Infrastructure/LeadQualification/ConfiguredDialogTemplateProvider.cs`
- Create: `tests/Burrfish.Application.Tests/LeadQualification/ProcessIncomingMessageCommandHandlerTests.cs`
- Create: `tests/Burrfish.Application.Tests/LeadQualification/ConfirmationClassifierTests.cs`

**Интерфейсы:**
- Consumes: `Conversation`, `IAiDialogClient`, `IDialogTemplateProvider`, `IConversationRepository`, `IOutboxWriter`, `ILeadRepository`. Infrastructure преобразует `DialogOptions` в `DialogTemplates`, поэтому Application не зависит от конфигурационных типов внешнего слоя.
- Produces: MediatR handler `ProcessIncomingMessageCommandHandler` и `DialogReply?`.

- [ ] **Step 1: написать таблицу unit-сценариев**

Покрыть `price`, `services`, уточнение, предложение передачи, «да», «нет», неоднозначное подтверждение, OpenAI failure fallback и `OperatorHandoff`. Проверить, что шаблон берётся из config, а текст ИИ не используется как цена или описание услуги.

- [ ] **Step 2: запустить узкий тест**

```powershell
dotnet test .\tests\Burrfish.Application.Tests\Burrfish.Application.Tests.csproj --filter FullyQualifiedName~ProcessIncomingMessageCommandHandlerTests
```

- [ ] **Step 3: реализовать orchestration без сетевых деталей**

```csharp
/// <summary>Исход обработки одного клиентского сообщения.</summary>
/// <param name="Text">Текст ответа; <see langword="null"/>, если бот должен молчать.</param>
/// <param name="Lead">Подтверждённая заявка; <see langword="null"/>, если заявка не создана.</param>
public sealed record DialogReply(string? Text, LeadDraft? Lead);
```

Сохранение изменённого диалога, заявки и outbox выполняется вызывающим persistence boundary в одной транзакции.

- [ ] **Step 4: пройти unit-набор**

```powershell
dotnet test .\tests\Burrfish.Application.Tests\Burrfish.Application.Tests.csproj --filter FullyQualifiedName~LeadQualification
dotnet test .\tests\Burrfish.Application.Tests\Burrfish.Application.Tests.csproj
```

---

### Task 8: Отправлять Instagram-ответы через outbox

**Результат:** каждый логический ответ становится одним outbox-заданием и безопасно повторяется при временной ошибке Meta.

**Файлы:**
- Create: `src/Burrfish.Application/Instagram/IInstagramMessageSender.cs`
- Create: `src/Burrfish.Application/Instagram/InstagramReply.cs`
- Create: `src/Burrfish.Application/Processing/DeliveryResult.cs`
- Create: `src/Burrfish.Infrastructure/Instagram/InstagramGraphApiClient.cs`
- Create: `src/Burrfish.Infrastructure/Processing/OutboxDispatcher.cs`
- Create: `src/Burrfish.Infrastructure/Processing/OutboxWorker.cs`
- Create: `tests/Burrfish.Infrastructure.Tests/Instagram/InstagramGraphApiClientTests.cs`
- Create: `tests/Burrfish.IntegrationTests/Processing/OutboxDispatcherTests.cs`

**Интерфейсы:**
- Consumes: `DialogReply.Text`, Instagram outbox payload.
- Produces: `IInstagramMessageSender.SendAsync(InstagramReply, CancellationToken)` и `DeliveryResult` со значениями `Sent`, `Retry`, `Failed`.

- [ ] **Step 1: написать контрактные тесты запроса Meta**

Проверить URL/метод по актуальной документации Meta, bearer token, recipient ID, JSON escaping, успешный response ID, `429`/`5xx` как retry и постоянный `4xx` как failed. Токен и body не должны попадать в сообщение лога.

- [ ] **Step 2: реализовать sender после падающего теста**

```powershell
dotnet test .\tests\Burrfish.Infrastructure.Tests\Burrfish.Infrastructure.Tests.csproj --filter FullyQualifiedName~InstagramGraphApiClientTests
```

- [ ] **Step 3: написать интеграционный тест dispatcher**

Один pending item захватывается одним worker; retry увеличивает attempt и задаёт будущий `NextAttemptAt`; sent item повторно не выбирается; постоянная ошибка сохраняет исходные код и сообщение Meta без сокрытия.

- [ ] **Step 4: пройти contract и integration проверки**

```powershell
dotnet test .\tests\Burrfish.Infrastructure.Tests\Burrfish.Infrastructure.Tests.csproj --filter FullyQualifiedName~Instagram
dotnet test .\tests\Burrfish.IntegrationTests\Burrfish.IntegrationTests.csproj --filter FullyQualifiedName~OutboxDispatcherTests
```

---

### Task 9: Атомарно создать заявку и уведомить Telegram

**Результат:** подтверждение сохраняет ровно одну заявку, переводит диалог в `OperatorHandoff` и создаёт одно Telegram-задание; Telegram outage не теряет заявку.

**Файлы:**
- Create: `src/Burrfish.Application/LeadQualification/CreateLeadCommand.cs`
- Create: `src/Burrfish.Application/LeadQualification/CreateLeadCommandHandler.cs`
- Create: `src/Burrfish.Domain/Leads/Lead.cs`
- Create: `src/Burrfish.Domain/Leads/LeadId.cs`
- Create: `src/Burrfish.Application/Telegram/ITelegramNotifier.cs`
- Create: `src/Burrfish.Application/Telegram/LeadNotification.cs`
- Create: `src/Burrfish.Application/Telegram/NotificationResult.cs`
- Create: `src/Burrfish.Infrastructure/Telegram/TelegramNotifier.cs`
- Create: `src/Burrfish.Infrastructure/Telegram/TelegramLeadMessageFormatter.cs`
- Create: `tests/Burrfish.Infrastructure.Tests/Telegram/TelegramLeadMessageFormatterTests.cs`
- Create: `tests/Burrfish.Infrastructure.Tests/Telegram/TelegramNotifierTests.cs`
- Create: `tests/Burrfish.Domain.Tests/Leads/LeadTests.cs`
- Create: `tests/Burrfish.IntegrationTests/LeadQualification/CreateLeadCommandHandlerTests.cs`

**Интерфейсы:**
- Consumes: `LeadDraft`, `TelegramOptions`, outbox dispatcher из Task 8.
- Produces: `CreateLeadCommandHandler`, `ITelegramNotifier.SendLeadAsync` и тип outbox `telegram.lead-created.v1`.

- [ ] **Step 1: написать форматтер-тесты**

Сообщение содержит UTC-время, потребность, sender ID и username только при наличии; не содержит историю; корректно экранирует Telegram markup; ссылка строится только когда допустимые данные Meta доступны.

- [ ] **Step 2: написать интеграционный тест транзакции**

Подтверждение создаёт одну lead row, состояние `OperatorHandoff` и один Telegram outbox. Повтор того же client message не создаёт второй lead. Искусственный rollback не сохраняет ни один из трёх результатов.

- [ ] **Step 3: реализовать минимальный сервис и notifier**

```csharp
/// <summary>Отправляет уведомления о подтверждённых заявках.</summary>
public interface ITelegramNotifier
{
    /// <summary>Отправляет заявку; возвращает ожидаемый интеграционный результат без исключения для HTTP-ошибок.</summary>
    Task<NotificationResult> SendLeadAsync(LeadNotification notification, CancellationToken cancellationToken);
}
```

- [ ] **Step 4: пройти проверки от узких к широким**

```powershell
dotnet test .\tests\Burrfish.Infrastructure.Tests\Burrfish.Infrastructure.Tests.csproj --filter FullyQualifiedName~TelegramLeadMessageFormatterTests
dotnet test .\tests\Burrfish.Infrastructure.Tests\Burrfish.Infrastructure.Tests.csproj --filter FullyQualifiedName~TelegramNotifierTests
dotnet test .\tests\Burrfish.IntegrationTests\Burrfish.IntegrationTests.csproj --filter FullyQualifiedName~CreateLeadCommandHandlerTests
```

---

### Task 10: Добавить retention, health и безопасную наблюдаемость

**Результат:** данные старше 30 календарных дней удаляются, состояние worker наблюдаемо, а журналы и метрики не раскрывают сообщения и секреты.

**Файлы:**
- Create: `src/Burrfish.Application/Common/IClock.cs`
- Create: `src/Burrfish.Infrastructure/Time/SystemClock.cs`
- Create: `src/Burrfish.Infrastructure/Persistence/DataRetentionService.cs`
- Create: `src/Burrfish.Infrastructure/Processing/WorkerHeartbeat.cs`
- Create: `src/Burrfish.Api/Configuration/HealthRegistration.cs`
- Create: `tests/Burrfish.IntegrationTests/Persistence/DataRetentionServiceTests.cs`
- Create: `tests/Burrfish.ApiContract.Tests/Health/HealthEndpointTests.cs`
- Create: `tests/Burrfish.Infrastructure.Tests/Logging/SensitiveLoggingTests.cs`

**Интерфейсы:**
- Consumes: все timestamped entities и worker statuses.
- Produces: `DataRetentionService.DeleteExpiredAsync`, `/health/live`, `/health/ready`, метрики счётчиков и длительности из спецификации.

- [ ] **Step 1: написать retention-тесты на фиксированном времени**

Записи ровно моложе 30 суток сохраняются; записи старше 30 суток удаляются, включая окончательно ошибочные и всё ещё pending outbox с клиентскими данными; после удаления `OperatorHandoff` новое сообщение создаёт `BotActive`. Перед удалением неотправленного задания записываются только технические код ошибки и счётчик без payload.

- [ ] **Step 2: реализовать retention и пройти узкий тест**

```powershell
dotnet test .\tests\Burrfish.IntegrationTests\Burrfish.IntegrationTests.csproj --filter FullyQualifiedName~DataRetentionServiceTests
```

- [ ] **Step 3: написать health-контракт**

`/health/live` проверяет процесс; `/health/ready` проверяет PostgreSQL и свежий heartbeat worker. OpenAI, Meta и Telegram не делают webhook unready. Endpoint не возвращает connection string или exception stack клиенту.

- [ ] **Step 4: проверить безопасные журналы**

Сгенерировать тестовые маркеры для message text и API tokens, выполнить error paths и убедиться, что маркеры отсутствуют в captured logs.

- [ ] **Step 5: пройти relevant-набор**

```powershell
dotnet test .\tests\Burrfish.Infrastructure.Tests\Burrfish.Infrastructure.Tests.csproj --filter FullyQualifiedName~SensitiveLoggingTests
dotnet test .\tests\Burrfish.ApiContract.Tests\Burrfish.ApiContract.Tests.csproj --filter FullyQualifiedName~HealthEndpointTests
dotnet test .\tests\Burrfish.IntegrationTests\Burrfish.IntegrationTests.csproj --filter FullyQualifiedName~DataRetentionServiceTests
```

---

### Task 11: Подготовить Docker-развёртывание и сквозную проверку

**Результат:** приложение собирается и запускается с PostgreSQL за HTTPS-прокси, а автоматизированный сквозной тест доказывает основной сценарий без реальных секретов.

**Файлы:**
- Create: `Dockerfile`
- Create: `.dockerignore`
- Create: `docker-compose.yml`
- Create: `deploy/Caddyfile`
- Create: `.env.example`
- Create: `README.md`
- Create: `tests/Burrfish.IntegrationTests/EndToEnd/LeadFlowTests.cs`
- Modify: `src/Burrfish.Api/Program.cs`

**Интерфейсы:**
- Consumes: все функции Tasks 1–10.
- Produces: production container, локальный compose stack и документированный runbook.

- [ ] **Step 1: написать сквозной тест с подменными HTTP API**

Тест отправляет подписанный fixture-вопрос о цене, ожидает утверждённый Instagram reply, отправляет подтверждение, ожидает ровно один Telegram call, затем отправляет третье сообщение и проверяет отсутствие Instagram reply. Повтор каждого webhook не меняет счётчики.

- [ ] **Step 2: запустить сквозной тест до wiring**

```powershell
dotnet test .\tests\Burrfish.IntegrationTests\Burrfish.IntegrationTests.csproj --filter FullyQualifiedName~LeadFlowTests
```

Ожидается `FAIL` до регистрации всех adapters и workers.

- [ ] **Step 3: добавить composition root и контейнеры**

Dockerfile использует multi-stage build .NET 10, непривилегированного пользователя и production runtime image. Compose содержит приложение, PostgreSQL и Caddy; healthchecks управляют порядком готовности. `.env.example` содержит только имена переменных и безопасные пустые значения.

- [ ] **Step 4: документировать запуск и внешние условия**

README описывает Meta Business-account/app review/permissions, настройку webhook, Telegram bot/chat, OpenAI key, DNS/TLS, migration application, backup, health endpoints и проверку первого диалога. Точные Meta permissions сверяются по официальной документации на момент реализации.

- [ ] **Step 5: выполнить проверки от узких к полным**

```powershell
dotnet test .\tests\Burrfish.IntegrationTests\Burrfish.IntegrationTests.csproj --filter FullyQualifiedName~LeadFlowTests
dotnet test .\Burrfish.slnx
dotnet build .\Burrfish.slnx --no-restore
docker compose config
docker compose build
git diff --check
```

Полный integration-набор и Docker-команды требуют доступного Docker daemon. Реальная smoke-проверка Meta выполняется только после предоставления runtime credentials вне репозитория.

## GitHub Issues

| Task | Заголовок issue | Зависимости | Проверяемый результат |
|---|---|---|---|
| 1 | Bootstrap .NET host and validate configuration | Нет | Host собирается, пустые обязательные настройки останавливают запуск без утечки значений |
| 2 | Implement conversation state machine | #1 | Заявка невозможна без подтверждения; после handoff бот не отвечает |
| 3 | Add PostgreSQL inbox, outbox, and deduplication | #1, #2 | Повторный event хранится один раз; lead/state/outbox атомарны |
| 4 | Implement verified Instagram webhook ingestion | #1, #3 | Verification и signature работают; валидный event сохраняется до HTTP 200 |
| 5 | Normalize Instagram events and process inbox | #2, #3, #4 | Текст нормализуется и обрабатывается один раз; unsupported event игнорируется |
| 6 | Add constrained OpenAI dialog client | #1 | Только разрешённые structured decisions проходят validation |
| 7 | Orchestrate intent templates and handoff confirmation | #2, #5, #6 | Price/services/fallback/confirmation дают детерминированные действия |
| 8 | Dispatch Instagram replies through reliable outbox | #3, #7 | Ответ отправляется один раз, transient Meta errors повторяются |
| 9 | Create leads and notify managers in Telegram | #3, #7, #8 | Lead, handoff и Telegram job атомарны; outage не теряет lead |
| 10 | Enforce retention and add safe health telemetry | #3, #5, #8, #9 | Данные удаляются через 30 дней; health и logs не раскрывают секреты |
| 11 | Package deployment and verify the end-to-end lead flow | #4–#10 | Docker stack запускается; основной сценарий и дедупликация проходят E2E |

## Порядок поставки

Tasks 1–2 дают тестируемую бизнес-основу без внешних сервисов. Tasks 3–5 формируют надёжный входной контур. Task 6 можно выполнять параллельно Tasks 3–5 после Task 1. Tasks 7–9 завершают пользовательский сценарий. Tasks 10–11 доводят решение до безопасного рабочего развёртывания.

Каждый issue закрывается только после узкого теста, всех затронутых тестовых проектов, сборки и `git diff --check`. Для Task 11 дополнительно обязательны `docker compose config` и `docker compose build`.
