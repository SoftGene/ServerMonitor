# Heartbeat — план реализации

**Goal:** Система сама сообщает, что машина перестала отчитываться, и что она вернулась.
Попутно проверка правил отделяется от доставки уведомлений: алертинг перестаёт зависеть от
того, настроен ли Telegram.

**Spec:** `docs/superpowers/specs/2026-09-07-heartbeat-design.md`

**Ветка:** `feature/heartbeat`

## Global Constraints

- Проекты: `ServerMonitor.Domain`, `ServerMonitor.Infrastructure`, `ServerMonitor.Api`,
  `ServerMonitor.Web`, `ServerMonitor.Tests`.
- Решения о срабатывании выносятся в **чистые функции** без БД и без Telegram — только так их
  можно покрыть тестами.
- Числа в JSON, SQL, CSS и SVG — через `CultureInfo.InvariantCulture`; числа для человека — в
  культуре пользователя.
- Запросы только на чтение — с `AsNoTracking()`, все обращения к БД асинхронные с
  `CancellationToken`.
- Существующее поведение пороговых алертов не меняется: те же гистерезис и сообщения.
- После каждой задачи: `dotnet build` без предупреждений, `dotnet test` зелёный, отдельный
  коммит без трейлера соавторства.

---

### Task 1: Домен — новое событие, настройки, настраиваемые пороги

**Files:**
- Modify: `ServerMonitor.Domain/Entities/Alert.cs` (значение `Availability`)
- Modify: `ServerMonitor.Domain/Entities/MetricKindExtensions.cs`
- Modify: `ServerMonitor.Domain/Entities/AppSettings.cs`
- Modify: `ServerMonitor.Domain/Entities/ServerHealth.cs` (пороги параметрами)
- Create: `ServerMonitor.Domain/Entities/HeartbeatRule.cs`
- Test: `ServerMonitor.Tests/Domain/HeartbeatRuleTests.cs`
- Test: `ServerMonitor.Tests/Domain/ServerHealthTests.cs` (дополнить)

**Interfaces:**
- Produces: `HeartbeatRule.Evaluate(bool isAlerting, DateTime? lastSeenUtc, DateTime nowUtc,
  TimeSpan offlineAfter)` → `AlertKind?`. Использует задача 4.

- [ ] **Step 1: Тесты правила (падающие)**

Правило — чистая функция без БД: на вход текущее состояние и время, на выходе «что изменилось».
`null` означает «ничего не поменялось, сообщать не о чем».

```csharp
using ServerMonitor.Domain.Entities;

namespace ServerMonitor.Tests.Domain;

public class HeartbeatRuleTests
{
    private static readonly DateTime Now = new(2026, 9, 7, 12, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Offline = TimeSpan.FromMinutes(5);

    [Fact]
    public void NeverReported_IsNotAnAlert()
    {
        // Агент зарегистрировался, но ни разу не прислал данные — это незаконченная
        // установка, а не авария.
        Assert.Null(HeartbeatRule.Evaluate(isAlerting: false, lastSeenUtc: null, Now, Offline));
    }

    [Fact]
    public void FreshData_WhileHealthy_ChangesNothing()
    {
        Assert.Null(HeartbeatRule.Evaluate(false, Now.AddSeconds(-10), Now, Offline));
    }

    [Fact]
    public void SilenceBeyondThreshold_Triggers()
    {
        Assert.Equal(AlertKind.Triggered,
            HeartbeatRule.Evaluate(false, Now.AddMinutes(-6), Now, Offline));
    }

    [Fact]
    public void SilenceContinuing_DoesNotRepeat()
    {
        // Сообщаем на переход, а не на состояние: иначе каждые 30 секунд приходило бы
        // «машина всё ещё недоступна».
        Assert.Null(HeartbeatRule.Evaluate(isAlerting: true, Now.AddMinutes(-40), Now, Offline));
    }

    [Fact]
    public void DataReturns_Recovers()
    {
        Assert.Equal(AlertKind.Recovered,
            HeartbeatRule.Evaluate(isAlerting: true, Now.AddSeconds(-3), Now, Offline));
    }

    [Fact]
    public void NeverReported_WhileAlerting_Recovers_Never()
    {
        // Машина без данных не «выздоравливает» сама по себе.
        Assert.Null(HeartbeatRule.Evaluate(isAlerting: true, null, Now, Offline));
    }

    [Theory]
    [InlineData(299, null)]                    // ещё в пределах порога
    [InlineData(301, AlertKind.Triggered)]     // уже за порогом
    public void ThresholdBoundary(int secondsAgo, AlertKind? expected)
    {
        Assert.Equal(expected,
            HeartbeatRule.Evaluate(false, Now.AddSeconds(-secondsAgo), Now, Offline));
    }
}
```

- [ ] **Step 2: Запустить — тесты падают**

Run: `dotnet test --filter HeartbeatRule`
Expected: ошибка компиляции — типа `HeartbeatRule` не существует.

- [ ] **Step 3: Реализовать правило**

`ServerMonitor.Domain/Entities/HeartbeatRule.cs`:

```csharp
namespace ServerMonitor.Domain.Entities;

/// <summary>
/// Решает, изменилось ли состояние доступности машины. Чистая функция: время приходит
/// параметром, поэтому результат не зависит от момента запуска и проверяется тестами.
/// </summary>
public static class HeartbeatRule
{
    /// <returns>
    /// <c>Triggered</c> — машина только что признана недоступной, <c>Recovered</c> — снова
    /// отвечает, <c>null</c> — ничего не изменилось и сообщать не о чем.
    /// </returns>
    public static AlertKind? Evaluate(
        bool isAlerting,
        DateTime? lastSeenUtc,
        DateTime nowUtc,
        TimeSpan offlineAfter)
    {
        if (lastSeenUtc is null)
        {
            // Данных не было никогда: ни поднимать тревогу, ни снимать её не о чем.
            return null;
        }

        var isSilent = nowUtc - lastSeenUtc.Value > offlineAfter;

        if (isSilent && !isAlerting)
        {
            return AlertKind.Triggered;
        }

        if (!isSilent && isAlerting)
        {
            return AlertKind.Recovered;
        }

        return null;
    }
}
```

- [ ] **Step 4: Значение перечисления и его имя**

В `Alert.cs` добавить в `MetricKind` значение `Availability` **последним** — порядок значений
не важен для хранения (в базе строки), но добавление в конец безопаснее для любого кода,
который вдруг полагается на числовые значения.

`MetricKindExtensions.ToDisplayName` менять не нужно: `Availability.ToString()` уже даёт
нужную строку. Проверить это тестом.

- [ ] **Step 5: Настройки**

В `AppSettings` добавить:

```csharp
/// <summary>Сколько секунд молчания считать пропажей машины. Тот же порог использует экран парка.</summary>
public int OfflineAfterSeconds { get; set; } = 300;

public bool HeartbeatAlertsEnabled { get; set; } = true;
```

- [ ] **Step 6: Пороги состояния — параметрами**

В `ServerHealthCalculator.FromLastSeen` добавить необязательные параметры, оставив нынешние
константы значениями по умолчанию:

```csharp
public static ServerHealth FromLastSeen(
    DateTime? lastSeenUtc,
    DateTime nowUtc,
    TimeSpan? staleAfter = null,
    TimeSpan? offlineAfter = null)
```

Дополнить `ServerHealthTests` случаем с нестандартным порогом.

- [ ] **Step 7: Тесты проходят**

Run: `dotnet test`
Expected: зелёный, тестов стало больше на 9–10.

- [ ] **Step 8: Commit**

```bash
git add -A && git commit -m "feat: heartbeat rule, availability alerts, configurable offline threshold"
```

---

### Task 2: Миграция настроек

**Files:**
- Modify: `ServerMonitor.Infrastructure/Data/AppDbContext.cs` (при необходимости)
- Create: миграция `AddHeartbeatSettings`

- [ ] **Step 1: Сгенерировать миграцию**

```bash
dotnet ef migrations add AddHeartbeatSettings --project ServerMonitor.Infrastructure --startup-project ServerMonitor.Api
```

- [ ] **Step 2: Проверить сгенерированный `Up`**

Две колонки с значениями по умолчанию (300 и `true`). Существующая строка настроек одна,
поэтому backfill не нужен — значения по умолчанию проставятся сами. Убедиться, что в
миграции именно `defaultValue`, а не `NOT NULL` без значения.

- [ ] **Step 3: Применить и проверить**

```bash
dotnet ef database update --project ServerMonitor.Infrastructure --startup-project ServerMonitor.Api
```

```bash
docker exec servermonitor-db psql -U monitor -d servermonitor -c 'SELECT * FROM "AppSettings";'
```

Expected: строка с `OfflineAfterSeconds = 300`, `HeartbeatAlertsEnabled = t`, остальные пороги
без изменений.

- [ ] **Step 4: Commit**

---

### Task 3: Канал доставки как абстракция

**Files:**
- Create: `ServerMonitor.Infrastructure/Alerting/AlertNotification.cs`
- Create: `ServerMonitor.Infrastructure/Alerting/IAlertChannel.cs`
- Create: `ServerMonitor.Infrastructure/Alerting/LogAlertChannel.cs`
- Create: `ServerMonitor.Infrastructure/Alerting/TelegramAlertChannel.cs`

**Interfaces:**
- Produces: `IAlertChannel.SendAsync(AlertNotification, CancellationToken)`. Использует задача 4.

- [ ] **Step 1: Модель уведомления**

```csharp
namespace ServerMonitor.Infrastructure.Alerting;

/// <summary>Что произошло — в виде, не зависящем от канала доставки.</summary>
public record AlertNotification(
    string ServerName,
    MetricKind Metric,
    AlertKind Kind,
    double Value,
    double Threshold);
```

- [ ] **Step 2: Интерфейс канала**

```csharp
public interface IAlertChannel
{
    Task SendAsync(AlertNotification notification, CancellationToken cancellationToken);
}
```

- [ ] **Step 3: Канал «журнал»**

Пишет уведомление в лог. Нужен, чтобы алертинг был наблюдаем без настроенного Telegram и чтобы
в тестовой среде всегда был хотя бы один канал.

- [ ] **Step 4: Канал Telegram**

Переносит сюда форматирование сообщений из `TelegramBotService`, включая экранирование имени
машины. Тексты для `Availability`:

```
🔌 <b>Machine unreachable</b> · {имя}
No data for {N} minutes (threshold {M} minutes)
```

```
🔄 <b>Machine back online</b> · {имя}
Reporting again after {N} minutes of silence
```

Если токен или чат не настроены — канал молча ничего не делает; это его нормальное состояние,
а не ошибка.

- [ ] **Step 5: Проверить**

Run: `dotnet build` → без предупреждений

- [ ] **Step 6: Commit**

---

### Task 4: AlertingService и похудевший бот

**Files:**
- Create: `ServerMonitor.Infrastructure/Alerting/AlertingService.cs`
- Create: `ServerMonitor.Infrastructure/Alerting/MetricAlertState.cs` (перенос из бота)
- Modify: `ServerMonitor.Infrastructure/Telegram/TelegramBotService.cs` (только команды)
- Modify: `ServerMonitor.Api/Program.cs` (регистрация)

**Interfaces:**
- Consumes: `HeartbeatRule` (задача 1), `IAlertChannel` (задача 3).

- [ ] **Step 1: Перенести проверку порогов**

Из `TelegramBotService` в `AlertingService` переезжают: словарь состояний по паре
«машина + метрика», восстановление состояния из журнала, обход серверов, `CheckThresholdAsync`,
`SaveAlertAsync`. Отправка идёт не напрямую в Telegram, а через `IEnumerable<IAlertChannel>`.

- [ ] **Step 2: Добавить проверку heartbeat**

В том же обходе серверов, **до** проверки порогов:

```csharp
var change = HeartbeatRule.Evaluate(
    state.IsAlerting, server.LastSeenUtc, nowUtc, offlineAfter);
```

При `Triggered`/`Recovered` — сохранить `Alert` с `MetricType = Availability`, `Value` = минут
молчания, `Threshold` = порог в минутах, и отправить в каналы.

- [ ] **Step 3: Окно тишины после старта**

Первый проход цикла пропускает проверку heartbeat (пороги проверяются как обычно).

```csharp
// После простоя API все машины выглядят пропавшими, хотя отчитаются через секунды.
// Пропускаем первый проход, давая агентам окно на то, чтобы объявиться.
if (isFirstPass)
{
    isFirstPass = false;
}
else { /* проверка heartbeat */ }
```

- [ ] **Step 4: Чистить состояние удалённых машин**

После обхода удалить из словаря состояния записи для машин, которых больше нет в списке.

- [ ] **Step 5: Похудеть бот**

В `TelegramBotService` остаётся: подключение, long polling, `HandleUpdateAsync`,
`GetStatusMessageAsync`, проверка чата. Удаляются: `_states`, `RestoreAlertStateAsync`,
`CheckMetricsAsync`, `CheckThresholdAsync`, `SaveAlertAsync`, `SendAlertAsync` и цикл проверки.

- [ ] **Step 6: Регистрация**

```csharp
builder.Services.AddSingleton<IAlertChannel, LogAlertChannel>();
builder.Services.AddSingleton<IAlertChannel, TelegramAlertChannel>();
builder.Services.AddHostedService<AlertingService>();
builder.Services.AddHostedService<TelegramBotService>();
```

- [ ] **Step 7: Проверить живьём**

Run: `dotnet build`, `dotnet test`

Запустить API и агента, затем **остановить агента** и дождаться порога:

```bash
docker exec servermonitor-db psql -U monitor -d servermonitor -c 'SELECT a."Id", s."Name", a."MetricType", a."AlertType", a."Value", a."Threshold" FROM "Alerts" a JOIN "Servers" s ON s."Id" = a."ServerId" ORDER BY a."Id" DESC LIMIT 3;'
```

Expected: запись `Availability | Triggered`. Затем запустить агента обратно — появляется
`Availability | Recovered`. Перезапустить API при живом агенте — ложных срабатываний нет.

- [ ] **Step 8: Commit**

---

### Task 5: Настройки и экран алертов

**Files:**
- Modify: `ServerMonitor.Api/Dtos/SettingsDto.cs`
- Modify: `ServerMonitor.Api/Controllers/SettingsController.cs`
- Modify: `ServerMonitor.Web/Models/AppSettings.cs`
- Modify: `ServerMonitor.Web/Components/Pages/Settings.razor`
- Modify: `ServerMonitor.Web/Components/Pages/Alerts.razor` (иконка `Availability`)

- [ ] **Step 1: Провести новые поля через API и модель фронтенда**

- [ ] **Step 2: Поля в форме настроек**

Порог в **минутах** для человека, в секундах в хранении — перевод в одном месте.

- [ ] **Step 3: Иконка события доступности**

Отличить `Availability` от пороговых событий: своя иконка (розетка/связь), чтобы лента
читалась с одного взгляда.

- [ ] **Step 4: Проверить в браузере**

- [ ] **Step 5: Commit**

---

### Task 6: Глава 11 гайда

**Files:**
- Create: `docs/guide/11-heartbeat-and-alerting.md`
- Modify: `docs/guide/README.md`
- Modify: `docs/guide/07-telegram-and-alerting.md` (проверка переехала)
- Modify: `README.md` (снять ограничение из списка)

- [ ] **Step 1: Написать главу**

Разделы: почему отсутствие данных — отдельный вид события; почему проверку нельзя повесить на
приём замера; разделение обязанностей и что такое «канал доставки»; чистая функция правила и
почему её удалось покрыть тестами; три ловушки (машина без данных, окно тишины после старта,
удалённая машина); чего система принципиально не может — сообщить о собственной смерти.

- [ ] **Step 2: Обновить затронутое**

В главе 07 отметить, что проверка порогов переехала в `AlertingService`. В README убрать из
списка ограничений пункты про отсутствие детекта пропажи и про привязку алертинга к Telegram.

- [ ] **Step 3: Commit**
