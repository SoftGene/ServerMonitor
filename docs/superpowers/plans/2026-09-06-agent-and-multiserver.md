# Агент и мульти-сервер — план реализации

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Вынести сбор метрик из процесса API в отдельного агента, который ставится на каждую машину, сам регистрируется по enrollment-токену и шлёт замеры в центральный API.

**Architecture:** Сбор переезжает в новый проект `ServerMonitor.Collection`, от него зависят агент и (временно) API. Появляется сущность `Server` с персональным API-ключом; `MetricSnapshot` и `Alert` получают `ServerId`. Агент — консольное приложение с `BackgroundService`, буфером в памяти и повторами с нарастающей паузой.

**Tech Stack:** .NET 10, EF Core 10 + Npgsql, ASP.NET Core, Blazor Server, xUnit.

**Spec:** `docs/superpowers/specs/2026-09-06-agent-and-multiserver-design.md`

## Global Constraints

- Проекты: `SeverMonitor.Domain` (папка с опечаткой, файл проекта `ServerMonitor.Domain.csproj`), `ServerMonitor.Collection` (новый), `ServerMonitor.Agent` (новый), `ServerMonitor.Infrastructure`, `ServerMonitor.Api`, `ServerMonitor.Web`, `ServerMonitor.Tests`.
- `TargetFramework` везде `net10.0`, `Nullable` и `ImplicitUsings` включены.
- Все числа, уходящие в JSON, SQL, CSS и SVG, форматируются через `CultureInfo.InvariantCulture`; числа для человека — в культуре пользователя.
- Все обращения к БД асинхронные, с передачей `CancellationToken`; запросы только на чтение — с `AsNoTracking()`.
- Секреты (enrollment-токен) не попадают в отслеживаемые git файлы: в `appsettings.json` пустой шаблон, значение в User Secrets.
- Визуальный язык интерфейса не меняется: токены тем, `--radius: 2px`, без теней и градиентов, mono для данных.
- После каждой задачи: `dotnet build` без предупреждений, `dotnet test` зелёный, отдельный коммит.
- Ветка `feature/redesign`.

---

### Task 1: Выделить проект ServerMonitor.Collection

**Files:**
- Create: `ServerMonitor.Collection/ServerMonitor.Collection.csproj`
- Move: `ServerMonitor.Infrastructure/Monitoring/{IMetricsCollector,MetricsCollector,ProcParser,CpuTimes}.cs` → `ServerMonitor.Collection/`
- Modify: `ServerMonitor.Infrastructure/ServerMonitor.Infrastructure.csproj` (добавить ProjectReference на Collection)
- Modify: `ServerMonitor.Tests/ServerMonitor.Tests.csproj` (добавить ProjectReference на Collection)
- Modify: `SeverMonitor.slnx`

**Interfaces:**
- Produces: пространство имён `ServerMonitor.Collection` с публичными `IMetricsCollector`, `MetricsCollector`, `ProcParser`, `CpuTimes` — их используют задачи 5 (агент) и существующий `MetricsCollectorService`.

- [x] **Step 1: Создать проект и подключить к решению**

```bash
dotnet new classlib -o ServerMonitor.Collection
```

```bash
dotnet add ServerMonitor.Collection reference SeverMonitor.Domain/ServerMonitor.Domain.csproj
```

```bash
dotnet sln SeverMonitor.slnx add ServerMonitor.Collection/ServerMonitor.Collection.csproj
```

Удалить сгенерированный `ServerMonitor.Collection/Class1.cs`.

- [x] **Step 2: Перенести файлы сбора**

Переместить четыре файла из `ServerMonitor.Infrastructure/Monitoring/` в `ServerMonitor.Collection/`, заменив в каждом строку пространства имён:

```csharp
namespace ServerMonitor.Collection;
```

`MonitoringOptions.cs` и `MetricsCollectorService.cs` **остаются** в `Infrastructure` (их делит задача 3).

- [x] **Step 3: Проставить ссылки**

В `ServerMonitor.Infrastructure.csproj` и `ServerMonitor.Tests.csproj` добавить:

```xml
<ProjectReference Include="..\ServerMonitor.Collection\ServerMonitor.Collection.csproj" />
```

- [x] **Step 4: Починить using в затронутых файлах**

Добавить `using ServerMonitor.Collection;` в:
- `ServerMonitor.Infrastructure/Monitoring/MetricsCollectorService.cs`
- `ServerMonitor.Api/Program.cs`
- `ServerMonitor.Tests/Monitoring/MetricsCollectorTests.cs`
- `ServerMonitor.Tests/Monitoring/ProcParserTests.cs`

- [x] **Step 5: Проверить, что перенос ничего не сломал**

Run: `dotnet build`
Expected: `Ошибок: 0`, `Предупреждений: 0`

Run: `SERVERMONITOR_RUN_COLLECTOR_TESTS=1 dotnet test`
Expected: `пройдено 27` — существующие тесты и есть проверка корректности переноса.

- [x] **Step 6: Commit**

```bash
git add -A && git commit -m "refactor: extract metric collection into ServerMonitor.Collection"
```

---

### Task 2: Сущность Server и миграция данных

**Files:**
- Create: `SeverMonitor.Domain/Entities/Server.cs`
- Modify: `SeverMonitor.Domain/Entities/MetricSnapshot.cs` (добавить `ServerId`)
- Modify: `SeverMonitor.Domain/Entities/Alert.cs` (добавить `ServerId`)
- Modify: `ServerMonitor.Infrastructure/Data/AppDbContext.cs`
- Create: миграция `AddServers`

**Interfaces:**
- Produces: `Server` с полями `Id`, `PublicId`, `Name`, `OperatingSystem`, `AgentVersion`, `ApiKeyHash`, `RegisteredAtUtc`, `LastSeenUtc`; `AppDbContext.Servers`. Используют задачи 4, 6.

- [x] **Step 1: Создать сущность**

```csharp
namespace ServerMonitor.Domain.Entities;

public class Server
{
    public int Id { get; set; }

    /// <summary>Идентификатор для URL и API: не раскрывает количество серверов.</summary>
    public Guid PublicId { get; set; }

    public string Name { get; set; } = string.Empty;
    public string? OperatingSystem { get; set; }
    public string? AgentVersion { get; set; }

    /// <summary>SHA-256 персонального ключа агента. Пусто — агент ещё не привязан.</summary>
    public string ApiKeyHash { get; set; } = string.Empty;

    public DateTime RegisteredAtUtc { get; set; }
    public DateTime? LastSeenUtc { get; set; }
}
```

- [x] **Step 2: Добавить ServerId в снапшот и алерт**

В `MetricSnapshot.cs` и `Alert.cs` добавить свойство:

```csharp
public int ServerId { get; set; }
```

- [x] **Step 3: Описать модель в AppDbContext**

Добавить `DbSet` и настройку:

```csharp
public DbSet<Server> Servers => Set<Server>();
```

В `OnModelCreating`:

```csharp
modelBuilder.Entity<Server>(entity =>
{
    entity.HasIndex(s => s.PublicId).IsUnique();
    entity.HasIndex(s => s.ApiKeyHash);
});

modelBuilder.Entity<MetricSnapshot>()
    .HasOne<Server>()
    .WithMany()
    .HasForeignKey(m => m.ServerId)
    .OnDelete(DeleteBehavior.Cascade);

modelBuilder.Entity<Alert>()
    .HasOne<Server>()
    .WithMany()
    .HasForeignKey(a => a.ServerId)
    .OnDelete(DeleteBehavior.Cascade);
```

- [x] **Step 4: Сгенерировать миграцию**

```bash
dotnet ef migrations add AddServers --project ServerMonitor.Infrastructure --startup-project ServerMonitor.Api
```

- [x] **Step 5: Дописать в миграцию привязку старых данных**

Сгенерированный `Up` создаст таблицу и добавит колонки со значением 0, что нарушит внешний ключ. Заменить тело `Up` так, чтобы порядок был: создать таблицу → вставить запись → добавить колонки → проставить значения → создать внешние ключи.

Между `CreateTable("Servers")` и созданием внешних ключей вставить:

```csharp
migrationBuilder.Sql("""
    INSERT INTO "Servers" ("PublicId", "Name", "ApiKeyHash", "RegisteredAtUtc")
    VALUES (gen_random_uuid(), 'this-machine', '', now() AT TIME ZONE 'utc');

    UPDATE "MetricSnapshots" SET "ServerId" = (SELECT MIN("Id") FROM "Servers");
    UPDATE "Alerts" SET "ServerId" = (SELECT MIN("Id") FROM "Servers");
    """);
```

Имя `this-machine` фиксированное: миграция — статический SQL, и `Environment.MachineName` записал бы имя машины разработчика в базу любого, кто развернёт проект.

- [x] **Step 6: Применить и проверить сохранность истории**

```bash
dotnet ef database update --project ServerMonitor.Infrastructure --startup-project ServerMonitor.Api
```

```bash
docker exec servermonitor-db psql -U monitor -d servermonitor -c 'SELECT count(*) FILTER (WHERE "ServerId" IS NULL OR "ServerId" = 0) AS orphans, count(*) AS total FROM "MetricSnapshots";'
```

Expected: `orphans = 0`, `total = 2021`.

- [x] **Step 7: Commit**

```bash
git add -A && git commit -m "feat: add Server entity and attach existing history to it"
```

---

### Task 3: Разделить MonitoringOptions

**Files:**
- Modify: `ServerMonitor.Infrastructure/Monitoring/MonitoringOptions.cs` (убрать интервал сбора)
- Modify: `ServerMonitor.Api/appsettings.json`
- Modify: `ServerMonitor.Infrastructure/Monitoring/MetricsCollectorService.cs`

**Interfaces:**
- Produces: `MonitoringOptions` только с `AlertCheckIntervalSeconds` и `AlertConsecutiveSamples`; агент (задача 5) получает свой `AgentOptions`.

- [x] **Step 1: Урезать MonitoringOptions**

Удалить `CollectIntervalSeconds` и `CollectInterval`. Оставшееся:

```csharp
public class MonitoringOptions
{
    public const string SectionName = "Monitoring";

    public int AlertCheckIntervalSeconds { get; set; } = 30;
    public int AlertConsecutiveSamples { get; set; } = 3;

    public TimeSpan AlertCheckInterval => TimeSpan.FromSeconds(Math.Max(1, AlertCheckIntervalSeconds));
    public int RequiredConsecutiveSamples => Math.Max(1, AlertConsecutiveSamples);
}
```

- [x] **Step 2: Вернуть константу в MetricsCollectorService**

Сервис удаляется на задаче 7, до тех пор ему нужен собственный интервал:

```csharp
private readonly TimeSpan _interval = TimeSpan.FromSeconds(5);
```

Убрать `IOptions<MonitoringOptions>` из его конструктора.

- [x] **Step 3: Убрать интервал сбора из конфигурации API**

В `ServerMonitor.Api/appsettings.json` из секции `Monitoring` удалить строку `"CollectIntervalSeconds": 5`.

- [x] **Step 4: Проверить**

Run: `dotnet build` → `Ошибок: 0`, `Предупреждений: 0`
Run: `dotnet test` → зелёный

- [x] **Step 5: Commit**

```bash
git add -A && git commit -m "refactor: split monitoring options between agent and alerting"
```

---

### Task 4: Регистрация агентов и приём метрик

**Files:**
- Create: `ServerMonitor.Infrastructure/Agents/ApiKeyGenerator.cs`
- Create: `ServerMonitor.Api/Dtos/AgentRegistrationRequest.cs`
- Create: `ServerMonitor.Api/Dtos/AgentRegistrationResponse.cs`
- Create: `ServerMonitor.Api/Dtos/MetricReportDto.cs`
- Create: `ServerMonitor.Api/Controllers/AgentsController.cs`
- Create: `ServerMonitor.Api/Controllers/IngestController.cs`
- Modify: `ServerMonitor.Api/appsettings.json` (секция `Agents`)
- Test: `ServerMonitor.Tests/Agents/ApiKeyGeneratorTests.cs`

**Interfaces:**
- Consumes: `Server` из задачи 2.
- Produces: `ApiKeyGenerator.Generate()` → `(string Key, string Hash)`, `ApiKeyGenerator.Hash(string key)` → `string`, `ApiKeyGenerator.FixedTimeEquals(string a, string b)` → `bool`. Использует задача 5.

- [x] **Step 1: Написать падающий тест на ключи**

```csharp
using ServerMonitor.Infrastructure.Agents;

namespace ServerMonitor.Tests.Agents;

public class ApiKeyGeneratorTests
{
    [Fact]
    public void Generate_ProducesDifferentKeysEveryTime()
    {
        var first = ApiKeyGenerator.Generate();
        var second = ApiKeyGenerator.Generate();

        Assert.NotEqual(first.Key, second.Key);
        Assert.NotEqual(first.Hash, second.Hash);
    }

    [Fact]
    public void Hash_IsStableForTheSameKey()
    {
        var generated = ApiKeyGenerator.Generate();

        Assert.Equal(generated.Hash, ApiKeyGenerator.Hash(generated.Key));
    }

    [Fact]
    public void Hash_DoesNotContainTheKeyItself()
    {
        var generated = ApiKeyGenerator.Generate();

        Assert.DoesNotContain(generated.Key, generated.Hash);
    }

    [Fact]
    public void FixedTimeEquals_ComparesValues()
    {
        Assert.True(ApiKeyGenerator.FixedTimeEquals("abc", "abc"));
        Assert.False(ApiKeyGenerator.FixedTimeEquals("abc", "abd"));
        Assert.False(ApiKeyGenerator.FixedTimeEquals("abc", "abcd"));
        Assert.False(ApiKeyGenerator.FixedTimeEquals("", "abc"));
    }
}
```

- [x] **Step 2: Запустить — тест падает**

Run: `dotnet test --filter ApiKeyGenerator`
Expected: ошибка компиляции — типа `ApiKeyGenerator` не существует.

- [x] **Step 3: Реализовать генератор**

```csharp
using System.Security.Cryptography;
using System.Text;

namespace ServerMonitor.Infrastructure.Agents;

public static class ApiKeyGenerator
{
    /// <summary>
    /// Создаёт ключ агента и его хеш. Ключ показывается один раз при регистрации,
    /// в базе остаётся только хеш.
    /// </summary>
    public static (string Key, string Hash) Generate()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        var key = Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        return (key, Hash(key));
    }

    /// <summary>
    /// SHA-256 без соли и растяжения: ключ — 32 случайных байта, словарного перебора
    /// не существует, поэтому медленный хеш (как для паролей) здесь не нужен.
    /// </summary>
    public static string Hash(string key)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        return Convert.ToHexString(hash);
    }

    /// <summary>Сравнение за постоянное время: длительность ответа не подсказывает атакующему, сколько символов угадано.</summary>
    public static bool FixedTimeEquals(string left, string right)
    {
        var leftBytes = Encoding.UTF8.GetBytes(left);
        var rightBytes = Encoding.UTF8.GetBytes(right);

        return CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }
}
```

- [x] **Step 4: Тест проходит**

Run: `dotnet test --filter ApiKeyGenerator`
Expected: `пройдено 4`

- [x] **Step 5: DTO контракта**

`AgentRegistrationRequest.cs`:

```csharp
namespace ServerMonitor.Api.Dtos;

public class AgentRegistrationRequest
{
    public string Hostname { get; set; } = string.Empty;
    public string? OperatingSystem { get; set; }
    public string? AgentVersion { get; set; }
}
```

`AgentRegistrationResponse.cs`:

```csharp
namespace ServerMonitor.Api.Dtos;

public class AgentRegistrationResponse
{
    public Guid ServerId { get; set; }
    public string ApiKey { get; set; } = string.Empty;
}
```

`MetricReportDto.cs`:

```csharp
namespace ServerMonitor.Api.Dtos;

public class MetricReportDto
{
    public DateTime TimestampUtc { get; set; }
    public double CpuUsagePercent { get; set; }
    public double MemoryUsedMb { get; set; }
    public double MemoryTotalMb { get; set; }
    public double DiskUsedGb { get; set; }
    public double DiskTotalGb { get; set; }
    public double UptimeSeconds { get; set; }
}
```

- [x] **Step 6: Контроллер регистрации**

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ServerMonitor.Api.Dtos;
using ServerMonitor.Domain.Entities;
using ServerMonitor.Infrastructure.Agents;
using ServerMonitor.Infrastructure.Data;

namespace ServerMonitor.Api.Controllers;

[ApiController]
[Route("api/agents")]
public class AgentsController : ControllerBase
{
    private readonly AppDbContext _dbContext;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AgentsController> _logger;

    public AgentsController(AppDbContext dbContext, IConfiguration configuration, ILogger<AgentsController> logger)
    {
        _dbContext = dbContext;
        _configuration = configuration;
        _logger = logger;
    }

    [HttpPost("register")]
    public async Task<ActionResult<AgentRegistrationResponse>> Register(
        [FromBody] AgentRegistrationRequest request,
        [FromHeader(Name = "X-Enrollment-Token")] string? enrollmentToken,
        CancellationToken cancellationToken)
    {
        var expectedToken = _configuration["Agents:EnrollmentToken"];

        // Без настроенного токена регистрация выключена: молча принимать кого угодно нельзя.
        if (string.IsNullOrWhiteSpace(expectedToken))
        {
            _logger.LogError("Agent registration is disabled: Agents:EnrollmentToken is not configured.");
            return StatusCode(StatusCodes.Status503ServiceUnavailable, "Agent registration is not configured.");
        }

        if (string.IsNullOrWhiteSpace(enrollmentToken) ||
            !ApiKeyGenerator.FixedTimeEquals(enrollmentToken, expectedToken))
        {
            _logger.LogWarning("Rejected agent registration with an invalid enrollment token.");
            return Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.Hostname))
        {
            return BadRequest("Hostname is required.");
        }

        var (key, hash) = ApiKeyGenerator.Generate();

        // Усыновление: миграция оставила запись со старой историей и без ключа.
        var orphans = await _dbContext.Servers
            .Where(s => s.ApiKeyHash == string.Empty)
            .ToListAsync(cancellationToken);

        Server server;

        if (orphans.Count == 1)
        {
            server = orphans[0];
            server.Name = request.Hostname;
            server.OperatingSystem = request.OperatingSystem;
            server.AgentVersion = request.AgentVersion;
            server.ApiKeyHash = hash;
        }
        else
        {
            server = new Server
            {
                PublicId = Guid.NewGuid(),
                Name = request.Hostname,
                OperatingSystem = request.OperatingSystem,
                AgentVersion = request.AgentVersion,
                ApiKeyHash = hash,
                RegisteredAtUtc = DateTime.UtcNow
            };

            _dbContext.Servers.Add(server);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Agent registered for server {Name} ({PublicId}).", server.Name, server.PublicId);

        return Ok(new AgentRegistrationResponse { ServerId = server.PublicId, ApiKey = key });
    }
}
```

- [x] **Step 7: Контроллер приёма**

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ServerMonitor.Api.Dtos;
using ServerMonitor.Domain.Entities;
using ServerMonitor.Infrastructure.Agents;
using ServerMonitor.Infrastructure.Data;

namespace ServerMonitor.Api.Controllers;

[ApiController]
[Route("api/ingest")]
public class IngestController : ControllerBase
{
    private const int MaxBatchSize = 1000;

    private readonly AppDbContext _dbContext;
    private readonly ILogger<IngestController> _logger;

    public IngestController(AppDbContext dbContext, ILogger<IngestController> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    [HttpPost]
    public async Task<IActionResult> Ingest(
        [FromBody] List<MetricReportDto> readings,
        [FromHeader(Name = "X-Api-Key")] string? apiKey,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return Unauthorized();
        }

        var hash = ApiKeyGenerator.Hash(apiKey);

        var server = await _dbContext.Servers
            .FirstOrDefaultAsync(s => s.ApiKeyHash == hash, cancellationToken);

        if (server is null)
        {
            _logger.LogWarning("Rejected ingest with an unknown API key.");
            return Unauthorized();
        }

        if (readings.Count == 0)
        {
            return BadRequest("At least one reading is required.");
        }

        if (readings.Count > MaxBatchSize)
        {
            return BadRequest($"At most {MaxBatchSize} readings per request.");
        }

        foreach (var reading in readings)
        {
            _dbContext.MetricSnapshots.Add(new MetricSnapshot
            {
                ServerId = server.Id,
                TimestampUtc = DateTime.SpecifyKind(reading.TimestampUtc, DateTimeKind.Utc),
                CpuUsagePercent = reading.CpuUsagePercent,
                MemoryUsedMb = reading.MemoryUsedMb,
                MemoryTotalMb = reading.MemoryTotalMb,
                DiskUsedGb = reading.DiskUsedGb,
                DiskTotalGb = reading.DiskTotalGb,
                UptimeSeconds = reading.UptimeSeconds
            });
        }

        server.LastSeenUtc = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        return Accepted();
    }
}
```

- [x] **Step 8: Конфигурация**

В `ServerMonitor.Api/appsettings.json` добавить пустой шаблон:

```json
"Agents": {
  "EnrollmentToken": ""
}
```

Реальное значение — в User Secrets:

```bash
dotnet user-secrets set "Agents:EnrollmentToken" "<случайная строка>" --project ServerMonitor.Api
```

- [x] **Step 9: Проверить эндпоинты запросами**

Запустить API, затем:

```bash
curl -sk -X POST https://localhost:7212/api/agents/register -H "Content-Type: application/json" -H "X-Enrollment-Token: <токен>" -d '{"hostname":"test","operatingSystem":"Windows","agentVersion":"1.0.0"}'
```

Expected: JSON с `serverId` и `apiKey`.

```bash
curl -sk -o /dev/null -w "%{http_code}\n" -X POST https://localhost:7212/api/agents/register -H "Content-Type: application/json" -H "X-Enrollment-Token: wrong" -d '{"hostname":"test"}'
```

Expected: `401`

- [x] **Step 10: Commit**

```bash
git add -A && git commit -m "feat: agent registration and metric ingest endpoints"
```

---

### Task 5: Проект агента

**Files:**
- Create: `ServerMonitor.Agent/ServerMonitor.Agent.csproj`
- Create: `ServerMonitor.Agent/AgentOptions.cs`
- Create: `ServerMonitor.Agent/AgentState.cs`
- Create: `ServerMonitor.Agent/MetricBuffer.cs`
- Create: `ServerMonitor.Agent/MetricReport.cs`
- Create: `ServerMonitor.Agent/AgentClient.cs`
- Create: `ServerMonitor.Agent/AgentWorker.cs`
- Create: `ServerMonitor.Agent/Program.cs`
- Create: `ServerMonitor.Agent/appsettings.json`
- Test: `ServerMonitor.Tests/AgentTests/MetricBufferTests.cs`
- Test: `ServerMonitor.Tests/AgentTests/AgentStateTests.cs`

**Interfaces:**
- Consumes: `IMetricsCollector`, `MetricsCollector` из задачи 1; контракт эндпоинтов из задачи 4.
- Produces: работающий агент; `MetricBuffer` с методами `Add(MetricReport)`, `Snapshot()` → `IReadOnlyList<MetricReport>`, `Remove(int count)`, свойство `Count`.

- [x] **Step 1: Тест на буфер (падающий)**

```csharp
using ServerMonitor.Agent;

namespace ServerMonitor.Tests.AgentTests;   // не ServerMonitor.Tests.Agent: имя Agent затеняло бы пространство имён агента

public class MetricBufferTests
{
    private static MetricReport Reading(double cpu) => new() { CpuUsagePercent = cpu };

    [Fact]
    public void Add_KeepsItemsInOrder()
    {
        var buffer = new MetricBuffer(capacity: 10);

        buffer.Add(Reading(1));
        buffer.Add(Reading(2));

        Assert.Equal(new double[] { 1, 2 }, buffer.Snapshot().Select(r => r.CpuUsagePercent));
    }

    [Fact]
    public void Add_DropsOldestWhenFull()
    {
        var buffer = new MetricBuffer(capacity: 2);

        buffer.Add(Reading(1));
        buffer.Add(Reading(2));
        buffer.Add(Reading(3));

        // Свежие данные ценнее исторических: выбрасывается самый старый замер.
        Assert.Equal(2, buffer.Count);
        Assert.Equal(new double[] { 2, 3 }, buffer.Snapshot().Select(r => r.CpuUsagePercent));
    }

    [Fact]
    public void Remove_DropsSentItemsFromTheFront()
    {
        var buffer = new MetricBuffer(capacity: 10);
        buffer.Add(Reading(1));
        buffer.Add(Reading(2));
        buffer.Add(Reading(3));

        buffer.Remove(2);

        Assert.Equal(new double[] { 3 }, buffer.Snapshot().Select(r => r.CpuUsagePercent));
    }

    [Fact]
    public void Remove_IgnoresCountLargerThanBuffer()
    {
        var buffer = new MetricBuffer(capacity: 10);
        buffer.Add(Reading(1));

        buffer.Remove(5);

        Assert.Equal(0, buffer.Count);
    }

    [Fact]
    public void Capacity_IsAtLeastOne()
    {
        var buffer = new MetricBuffer(capacity: 0);

        buffer.Add(Reading(1));

        Assert.Equal(1, buffer.Count);
    }
}
```

- [x] **Step 2: Создать проект агента**

```bash
dotnet new worker -o ServerMonitor.Agent
```

```bash
dotnet add ServerMonitor.Agent reference ServerMonitor.Collection/ServerMonitor.Collection.csproj
```

```bash
dotnet sln SeverMonitor.slnx add ServerMonitor.Agent/ServerMonitor.Agent.csproj
```

```bash
dotnet add ServerMonitor.Tests reference ServerMonitor.Agent/ServerMonitor.Agent.csproj
```

Удалить сгенерированный `Worker.cs`.

- [x] **Step 3: Модель отчёта и буфер**

`MetricReport.cs`:

```csharp
namespace ServerMonitor.Agent;

/// <summary>Замер в том виде, в каком он уходит на сервер. Совпадает с MetricReportDto в API.</summary>
public class MetricReport
{
    public DateTime TimestampUtc { get; set; }
    public double CpuUsagePercent { get; set; }
    public double MemoryUsedMb { get; set; }
    public double MemoryTotalMb { get; set; }
    public double DiskUsedGb { get; set; }
    public double DiskTotalGb { get; set; }
    public double UptimeSeconds { get; set; }
}
```

`MetricBuffer.cs`:

```csharp
namespace ServerMonitor.Agent;

/// <summary>
/// Очередь замеров, не отправленных на сервер. При переполнении выбрасывает самый старый:
/// свежие данные ценнее исторических.
/// </summary>
public class MetricBuffer
{
    private readonly Queue<MetricReport> _items = new();
    private readonly int _capacity;
    private readonly Lock _sync = new();

    public MetricBuffer(int capacity)
    {
        _capacity = Math.Max(1, capacity);
    }

    public int Count
    {
        get
        {
            lock (_sync)
            {
                return _items.Count;
            }
        }
    }

    public void Add(MetricReport report)
    {
        lock (_sync)
        {
            while (_items.Count >= _capacity)
            {
                _items.Dequeue();
            }

            _items.Enqueue(report);
        }
    }

    public IReadOnlyList<MetricReport> Snapshot()
    {
        lock (_sync)
        {
            return _items.ToList();
        }
    }

    public void Remove(int count)
    {
        lock (_sync)
        {
            for (var i = 0; i < count && _items.Count > 0; i++)
            {
                _items.Dequeue();
            }
        }
    }
}
```

- [x] **Step 4: Тест буфера проходит**

Run: `dotnet test --filter MetricBuffer`
Expected: `пройдено 5`

- [x] **Step 5: Настройки и состояние**

`AgentOptions.cs`:

```csharp
namespace ServerMonitor.Agent;

public class AgentOptions
{
    public const string SectionName = "Agent";

    public string ServerUrl { get; set; } = string.Empty;
    public string EnrollmentToken { get; set; } = string.Empty;
    public int CollectIntervalSeconds { get; set; } = 5;
    public int BufferCapacity { get; set; } = 720;
    public int MaxRetryDelaySeconds { get; set; } = 300;

    public TimeSpan CollectInterval => TimeSpan.FromSeconds(Math.Max(1, CollectIntervalSeconds));
    public TimeSpan MaxRetryDelay => TimeSpan.FromSeconds(Math.Max(1, MaxRetryDelaySeconds));
}
```

`AgentState.cs`:

```csharp
using System.Runtime.InteropServices;
using System.Text.Json;

namespace ServerMonitor.Agent;

/// <summary>Выданные при регистрации идентификатор и ключ. Хранятся рядом с бинарником.</summary>
public class AgentState
{
    public Guid ServerId { get; set; }
    public string ApiKey { get; set; } = string.Empty;

    public static async Task<AgentState?> LoadAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<AgentState>(stream, cancellationToken: cancellationToken);
    }

    public async Task SaveAsync(string path, CancellationToken cancellationToken)
    {
        await using (var stream = File.Create(path))
        {
            await JsonSerializer.SerializeAsync(stream, this, cancellationToken: cancellationToken);
        }

        // Ключ — секрет: на Unix закрываем файл от всех, кроме владельца.
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }
}
```

- [x] **Step 6: Тест состояния**

```csharp
using ServerMonitor.Agent;

namespace ServerMonitor.Tests.AgentTests;   // не ServerMonitor.Tests.Agent: имя Agent затеняло бы пространство имён агента

public class AgentStateTests
{
    [Fact]
    public async Task LoadAsync_ReturnsNullWhenFileIsMissing()
    {
        var path = Path.Combine(Path.GetTempPath(), $"agent-state-{Guid.NewGuid():N}.json");

        Assert.Null(await AgentState.LoadAsync(path, CancellationToken.None));
    }

    [Fact]
    public async Task SaveAsync_ThenLoadAsync_RoundTripsTheState()
    {
        var path = Path.Combine(Path.GetTempPath(), $"agent-state-{Guid.NewGuid():N}.json");
        var state = new AgentState { ServerId = Guid.NewGuid(), ApiKey = "secret-key" };

        try
        {
            await state.SaveAsync(path, CancellationToken.None);
            var loaded = await AgentState.LoadAsync(path, CancellationToken.None);

            Assert.NotNull(loaded);
            Assert.Equal(state.ServerId, loaded.ServerId);
            Assert.Equal(state.ApiKey, loaded.ApiKey);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
```

Run: `dotnet test --filter AgentState`
Expected: `пройдено 2`

- [x] **Step 7: Клиент API**

`AgentClient.cs`:

```csharp
using System.Net.Http.Json;

namespace ServerMonitor.Agent;

/// <summary>Обёртка над HTTP-вызовами к серверу мониторинга.</summary>
public class AgentClient
{
    private readonly HttpClient _httpClient;

    public AgentClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<AgentState> RegisterAsync(string enrollmentToken, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "api/agents/register")
        {
            Content = JsonContent.Create(new
            {
                hostname = Environment.MachineName,
                operatingSystem = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
                agentVersion = typeof(AgentClient).Assembly.GetName().Version?.ToString() ?? "unknown"
            })
        };

        request.Headers.Add("X-Enrollment-Token", enrollmentToken);

        var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var state = await response.Content.ReadFromJsonAsync<AgentState>(cancellationToken);

        return state ?? throw new InvalidOperationException("Registration response was empty.");
    }

    public async Task SendAsync(string apiKey, IReadOnlyList<MetricReport> readings, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "api/ingest")
        {
            Content = JsonContent.Create(readings)
        };

        request.Headers.Add("X-Api-Key", apiKey);

        var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}
```

Ответ регистрации приходит с полями `serverId` и `apiKey` — они совпадают со свойствами `AgentState`, разбор регистронезависимый.

- [x] **Step 8: Рабочий цикл**

`AgentWorker.cs`:

```csharp
using Microsoft.Extensions.Options;
using ServerMonitor.Collection;

namespace ServerMonitor.Agent;

public class AgentWorker : BackgroundService
{
    private readonly IMetricsCollector _collector;
    private readonly AgentClient _client;
    private readonly AgentOptions _options;
    private readonly ILogger<AgentWorker> _logger;
    private readonly MetricBuffer _buffer;
    private readonly string _statePath;

    public AgentWorker(
        IMetricsCollector collector,
        AgentClient client,
        IOptions<AgentOptions> options,
        ILogger<AgentWorker> logger)
    {
        _collector = collector;
        _client = client;
        _options = options.Value;
        _logger = logger;
        _buffer = new MetricBuffer(_options.BufferCapacity);
        _statePath = Path.Combine(AppContext.BaseDirectory, "agent-state.json");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var state = await EnsureRegisteredAsync(stoppingToken);

        if (state is null)
        {
            return;
        }

        var retryDelay = _options.CollectInterval;

        while (!stoppingToken.IsCancellationRequested)
        {
            var sent = false;

            try
            {
                var snapshot = await _collector.CollectAsync(stoppingToken);

                _buffer.Add(new MetricReport
                {
                    TimestampUtc = snapshot.TimestampUtc,
                    CpuUsagePercent = snapshot.CpuUsagePercent,
                    MemoryUsedMb = snapshot.MemoryUsedMb,
                    MemoryTotalMb = snapshot.MemoryTotalMb,
                    DiskUsedGb = snapshot.DiskUsedGb,
                    DiskTotalGb = snapshot.DiskTotalGb,
                    UptimeSeconds = snapshot.UptimeSeconds
                });

                var pending = _buffer.Snapshot();

                await _client.SendAsync(state.ApiKey, pending, stoppingToken);

                _buffer.Remove(pending.Count);
                sent = true;

                if (pending.Count > 1)
                {
                    _logger.LogInformation("Sent {Count} buffered readings after reconnect.", pending.Count);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send readings; {Count} kept in the buffer.", _buffer.Count);
            }

            // Успех — обычный интервал; неудача — растущая пауза, чтобы не долбить лежащий сервер.
            retryDelay = sent
                ? _options.CollectInterval
                : Min(retryDelay + retryDelay, _options.MaxRetryDelay);

            try
            {
                await Task.Delay(retryDelay, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }
    }

    private static TimeSpan Min(TimeSpan left, TimeSpan right) => left < right ? left : right;

    private async Task<AgentState?> EnsureRegisteredAsync(CancellationToken cancellationToken)
    {
        var state = await AgentState.LoadAsync(_statePath, cancellationToken);

        if (state is not null)
        {
            _logger.LogInformation("Agent already registered as {ServerId}.", state.ServerId);
            return state;
        }

        if (string.IsNullOrWhiteSpace(_options.EnrollmentToken))
        {
            _logger.LogError("Agent is not registered and Agent:EnrollmentToken is not configured. Nothing to do.");
            return null;
        }

        try
        {
            state = await _client.RegisterAsync(_options.EnrollmentToken, cancellationToken);
            await state.SaveAsync(_statePath, cancellationToken);

            _logger.LogInformation("Agent registered as {ServerId}.", state.ServerId);

            return state;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Registration failed.");
            return null;
        }
    }
}
```

- [x] **Step 9: Точка входа и конфигурация**

`Program.cs`:

```csharp
using ServerMonitor.Agent;
using ServerMonitor.Collection;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<AgentOptions>(
    builder.Configuration.GetSection(AgentOptions.SectionName));

var serverUrl = builder.Configuration["Agent:ServerUrl"];

if (string.IsNullOrWhiteSpace(serverUrl))
{
    throw new InvalidOperationException(
        "Agent:ServerUrl is not configured. Set it in appsettings.json or via the Agent__ServerUrl environment variable.");
}

builder.Services.AddSingleton<IMetricsCollector, MetricsCollector>();
builder.Services.AddHttpClient<AgentClient>(client => client.BaseAddress = new Uri(serverUrl));
builder.Services.AddHostedService<AgentWorker>();

var host = builder.Build();
host.Run();
```

`appsettings.json`:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.Hosting.Lifetime": "Information"
    }
  },
  "Agent": {
    "ServerUrl": "https://localhost:7212",
    "EnrollmentToken": "",
    "CollectIntervalSeconds": 5,
    "BufferCapacity": 720,
    "MaxRetryDelaySeconds": 300
  }
}
```

- [x] **Step 10: Запустить агента и убедиться, что данные доходят**

Задать токен агенту:

```bash
dotnet user-secrets init --project ServerMonitor.Agent
```

```bash
dotnet user-secrets set "Agent:EnrollmentToken" "<тот же токен, что у API>" --project ServerMonitor.Agent
```

Запустить API, затем агента; проверить в базе:

```bash
docker exec servermonitor-db psql -U monitor -d servermonitor -c 'SELECT s."Name", s."AgentVersion", s."LastSeenUtc", count(m.*) FROM "Servers" s LEFT JOIN "MetricSnapshots" m ON m."ServerId" = s."Id" GROUP BY s."Id", s."Name", s."AgentVersion", s."LastSeenUtc";'
```

Expected: запись с настоящим именем машины, свежим `LastSeenUtc` и растущим числом замеров.

- [x] **Step 11: Commit**

```bash
git add -A && git commit -m "feat: standalone monitoring agent with buffering and self-registration"
```

---

### Task 6: Эндпоинты чтения под сервер и экран парка

**Files:**
- Create: `ServerMonitor.Api/Dtos/ServerSummaryDto.cs`
- Create: `ServerMonitor.Api/Controllers/ServersController.cs`
- Modify: `ServerMonitor.Api/Controllers/MetricsController.cs` (маршрут под сервер)
- Modify: `ServerMonitor.Api/Controllers/AlertsController.cs` (фильтр по серверу)
- Create: `ServerMonitor.Web/Models/ServerSummary.cs`
- Modify: `ServerMonitor.Web/Services/MetricsApiClient.cs`
- Create: `ServerMonitor.Web/Components/Pages/Fleet.razor` + `.razor.css`
- Modify: `ServerMonitor.Web/Components/Pages/Dashboard.razor` (маршрут `/servers/{PublicId:guid}`)
- Modify: `ServerMonitor.Web/Components/Layout/TopNav.razor` (пункт «Fleet»)
- Test: `ServerMonitor.Tests/Domain/ServerHealthTests.cs`

**Interfaces:**
- Consumes: `Server`, эндпоинты задач 2 и 4.
- Produces: `ServerHealth.FromLastSeen(DateTime? lastSeenUtc, DateTime nowUtc)` → `ServerHealth` (`Online`, `Stale`, `Offline`).

- [x] **Step 1: Тест на вычисление статуса (падающий)**

```csharp
using ServerMonitor.Domain.Entities;

namespace ServerMonitor.Tests.Domain;

public class ServerHealthTests
{
    private static readonly DateTime Now = new(2026, 9, 6, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void NeverSeen_IsOffline()
    {
        Assert.Equal(ServerHealth.Offline, ServerHealthCalculator.FromLastSeen(null, Now));
    }

    [Theory]
    [InlineData(0, ServerHealth.Online)]
    [InlineData(59, ServerHealth.Online)]
    [InlineData(61, ServerHealth.Stale)]
    [InlineData(299, ServerHealth.Stale)]
    [InlineData(301, ServerHealth.Offline)]
    public void HealthDependsOnDataAge(int secondsAgo, ServerHealth expected)
    {
        var lastSeen = Now.AddSeconds(-secondsAgo);

        Assert.Equal(expected, ServerHealthCalculator.FromLastSeen(lastSeen, Now));
    }
}
```

- [x] **Step 2: Реализовать расчёт статуса**

`SeverMonitor.Domain/Entities/ServerHealth.cs`:

```csharp
namespace ServerMonitor.Domain.Entities;

public enum ServerHealth
{
    Online,
    Stale,
    Offline
}

public static class ServerHealthCalculator
{
    /// <summary>Данных нет дольше минуты — подозрительно, дольше пяти — сервер считаем недоступным.</summary>
    public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan OfflineAfter = TimeSpan.FromMinutes(5);

    public static ServerHealth FromLastSeen(DateTime? lastSeenUtc, DateTime nowUtc)
    {
        if (lastSeenUtc is null)
        {
            return ServerHealth.Offline;
        }

        var age = nowUtc - lastSeenUtc.Value;

        if (age > OfflineAfter) return ServerHealth.Offline;
        if (age > StaleAfter) return ServerHealth.Stale;

        return ServerHealth.Online;
    }
}
```

Run: `dotnet test --filter ServerHealth`
Expected: `пройдено 6`

- [x] **Step 3: DTO списка серверов**

```csharp
namespace ServerMonitor.Api.Dtos;

public class ServerSummaryDto
{
    public Guid PublicId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? OperatingSystem { get; set; }
    public string? AgentVersion { get; set; }
    public DateTime? LastSeenUtc { get; set; }
    public string Health { get; set; } = string.Empty;
    public double? CpuUsagePercent { get; set; }
    public double? MemoryUsagePercent { get; set; }
    public double? DiskUsagePercent { get; set; }
}
```

- [x] **Step 4: Контроллер серверов**

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ServerMonitor.Api.Dtos;
using ServerMonitor.Domain.Entities;
using ServerMonitor.Infrastructure.Data;

namespace ServerMonitor.Api.Controllers;

[ApiController]
[Route("api/servers")]
public class ServersController : ControllerBase
{
    private readonly AppDbContext _dbContext;

    public ServersController(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet]
    public async Task<ActionResult<List<ServerSummaryDto>>> GetServers(CancellationToken cancellationToken)
    {
        var servers = await _dbContext.Servers
            .AsNoTracking()
            .OrderBy(s => s.Name)
            .ToListAsync(cancellationToken);

        var now = DateTime.UtcNow;
        var result = new List<ServerSummaryDto>(servers.Count);

        foreach (var server in servers)
        {
            var latest = await _dbContext.MetricSnapshots
                .AsNoTracking()
                .Where(m => m.ServerId == server.Id)
                .OrderByDescending(m => m.TimestampUtc)
                .FirstOrDefaultAsync(cancellationToken);

            result.Add(new ServerSummaryDto
            {
                PublicId = server.PublicId,
                Name = server.Name,
                OperatingSystem = server.OperatingSystem,
                AgentVersion = server.AgentVersion,
                LastSeenUtc = server.LastSeenUtc,
                Health = ServerHealthCalculator.FromLastSeen(server.LastSeenUtc, now).ToString(),
                CpuUsagePercent = latest?.CpuUsagePercent,
                MemoryUsagePercent = latest?.MemoryUsagePercent,
                DiskUsagePercent = latest?.DiskUsagePercent
            });
        }

        return Ok(result);
    }

    [HttpDelete("{publicId:guid}")]
    public async Task<IActionResult> DeleteServer(Guid publicId, CancellationToken cancellationToken)
    {
        var server = await _dbContext.Servers
            .FirstOrDefaultAsync(s => s.PublicId == publicId, cancellationToken);

        if (server is null)
        {
            return NotFound();
        }

        // Каскад удалит метрики и алерты этого сервера.
        _dbContext.Servers.Remove(server);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return NoContent();
    }
}
```

- [x] **Step 5: Перевести MetricsController под сервер**

Заменить атрибут маршрута класса:

```csharp
[Route("api/servers/{publicId:guid}/metrics")]
```

В каждый метод добавить первым параметром `Guid publicId` и в начале — разрешение сервера:

```csharp
var serverId = await _dbContext.Servers
    .AsNoTracking()
    .Where(s => s.PublicId == publicId)
    .Select(s => (int?)s.Id)
    .FirstOrDefaultAsync(cancellationToken);

if (serverId is null)
{
    return NotFound("Server not found.");
}
```

и в каждый запрос метрик добавить `.Where(m => m.ServerId == serverId)`.

- [x] **Step 6: Фильтр по серверу в алертах**

В `AlertsController.GetAlerts` добавить параметр и фильтр:

```csharp
[FromQuery] Guid? serverId = null
```

```csharp
IQueryable<Alert> query = _dbContext.Alerts.AsNoTracking();

if (serverId is not null)
{
    var id = await _dbContext.Servers
        .AsNoTracking()
        .Where(s => s.PublicId == serverId)
        .Select(s => (int?)s.Id)
        .FirstOrDefaultAsync(cancellationToken);

    if (id is null)
    {
        return NotFound("Server not found.");
    }

    query = query.Where(a => a.ServerId == id);
}
```

- [x] **Step 7: Клиент во фронтенде**

`ServerMonitor.Web/Models/ServerSummary.cs` — копия `ServerSummaryDto` (тот же приём, что и для остальных моделей: связь только через форму JSON).

В `MetricsApiClient` добавить метод и провести `publicId` через существующие:

```csharp
public async Task<List<ServerSummary>> GetServersAsync(CancellationToken cancellationToken = default)
{
    var servers = await _httpClient.GetFromJsonAsync<List<ServerSummary>>("api/servers", cancellationToken);
    return servers ?? new List<ServerSummary>();
}
```

Существующие методы получают первым параметром `Guid serverId`, а адреса становятся
`api/servers/{serverId}/metrics/...`.

- [x] **Step 8: Страница парка**

`Fleet.razor` — `@page "/"` и `@page "/servers"`, таблица-«стойка» из ячеек по образцу `.metric-panel`: имя сервера, ОС, статус точкой в цвете `--status-ok/warn/crit`, три текущих значения, время последнего замера. Ячейка — ссылка на `/servers/{publicId}`.

Стили — в `Fleet.razor.css`, повторяя приёмы `Dashboard.razor.css`: `gap: 1px` на фоне `var(--border)`, `--radius`, mono для чисел.

- [x] **Step 9: Дашборд как детальная страница**

В `Dashboard.razor` заменить директиву страницы:

```razor
@page "/servers/{PublicId:guid}"
```

и добавить параметр:

```csharp
[Parameter] public Guid PublicId { get; set; }
```

Все вызовы `ApiClient` получают `PublicId`. В шапку добавить ссылку «← Fleet».

- [x] **Step 10: Пункт меню**

В `TopNav.razor` заменить пункт `Dashboard` на `Fleet` со ссылкой `href=""` и `Match="NavLinkMatch.All"`.

- [x] **Step 11: Проверить в браузере**

Запустить API, агента и Web. На `/` — список серверов со статусами; клик открывает дашборд машины; History и Alerts работают.

- [x] **Step 12: Commit**

```bash
git add -A && git commit -m "feat: fleet screen and per-server read endpoints"
```

---

### Task 7: Удалить встроенный сбор из API

**Files:**
- Delete: `ServerMonitor.Infrastructure/Monitoring/MetricsCollectorService.cs`
- Modify: `ServerMonitor.Api/Program.cs`
- Modify: `ServerMonitor.Infrastructure/ServerMonitor.Infrastructure.csproj` (убрать ссылку на Collection)
- Modify: `ServerMonitor.Api/ServerMonitor.Api.csproj` (убрать ссылку на Collection, если появлялась)

**Interfaces:**
- Consumes: работающий агент из задачи 5 — он полностью заменяет удаляемый сервис.

- [x] **Step 1: Убедиться, что агент работает**

```bash
docker exec servermonitor-db psql -U monitor -d servermonitor -c 'SELECT max("TimestampUtc") FROM "MetricSnapshots";'
```

Expected: метка времени не старше минуты — данные идут от агента, удаление сбора ничего не оборвёт.

- [x] **Step 2: Удалить сервис и его регистрацию**

Удалить файл `MetricsCollectorService.cs`. Из `ServerMonitor.Api/Program.cs` убрать строки:

```csharp
builder.Services.AddSingleton<IMetricsCollector, MetricsCollector>();
builder.Services.AddHostedService<MetricsCollectorService>();
```

и соответствующий `using ServerMonitor.Collection;`.

- [x] **Step 3: Убрать ссылку на Collection**

Удалить `<ProjectReference>` на `ServerMonitor.Collection` из `ServerMonitor.Infrastructure.csproj`.

- [x] **Step 4: Проверить**

Run: `dotnet build` → `Ошибок: 0`, `Предупреждений: 0`
Run: `dotnet test` → зелёный

Запустить API и агента, убедиться, что новые замеры продолжают появляться и в API-логах больше нет строк `Snapshot saved`.

- [x] **Step 5: Commit**

```bash
git add -A && git commit -m "refactor: remove in-process collection from the API"
```

---

### Task 8: Глава 10 гайда

**Files:**
- Create: `docs/guide/10-agent-and-multiserver.md`
- Modify: `docs/guide/README.md` (строка в оглавлении)
- Modify: `docs/guide/00-project-map.md` (карта проектов, поток данных, снятое ограничение)
- Modify: `docs/guide/04-background-services.md` (сбор больше не в API)

**Interfaces:**
- Consumes: весь код задач 1–7.

- [ ] **Step 1: Написать главу**

Разделы: зачем агент и что он снимает; новая карта проектов и почему `Collection` отдельно; сущность `Server`, `PublicId` против `Id`; миграция и правило усыновления; два вида секретов и почему SHA-256 без соли достаточно для ключей; сравнение за постоянное время; буфер и почему выбрасывается старейший; экспоненциальная задержка; вложенные маршруты API; экран парка.

Стиль — как в остальных главах: только реальный код со ссылками на файлы, блоки «Почему так» и «Слабое место», термины с английскими дублями, схемы mermaid.

- [ ] **Step 2: Обновить затронутые главы**

В `00-project-map.md` — таблица проектов (добавить `Collection`, `Agent`, `Tests`), диаграмма зависимостей, путь данных через HTTP-приём; убрать формулировку про «главное ограничение — одна машина». В `04-background-services.md` — отметить, что сбор переехал в агента.

- [ ] **Step 3: Commit**

```bash
git add docs/ && git commit -m "docs: guide chapter 10 on the agent and multi-server support"
```
