# Глава 01 — C# и .NET на примерах нашего кода

В этой главе разбирается сам язык: всё, что встречается в проекте, — от объявления класса до
`async/await` и LINQ. Примеры взяты из реальных файлов, ничего выдуманного.

Главу можно читать подряд или использовать как справочник: наткнулся в коде на непонятную
конструкцию — нашёл раздел.

---

## Часть 1. Платформа

### Что такое .NET

**C#** — язык программирования. **.NET** — платформа, на которой он выполняется. Разделять
их важно: на .NET работают ещё F# и Visual Basic, а C# без .NET не существует.

Что происходит, когда ты нажимаешь «собрать»:

```mermaid
flowchart LR
    src["Исходный код<br/>*.cs"] -->|"компилятор Roslyn"| il["Промежуточный код<br/>IL, файл .dll"]
    il -->|"JIT при запуске"| mc["Машинный код<br/>конкретного процессора"]
```

1. Компилятор переводит `.cs` в **промежуточный язык** (Intermediate Language, IL) — это не
   машинный код, а инструкции для виртуальной машины. Они лежат в `.dll`.
2. При запуске **среда выполнения** (Common Language Runtime, CLR) компилирует IL в
   машинный код прямо во время работы — это называется **JIT-компиляция** (Just-In-Time,
   «точно вовремя»).

Отсюда практическое следствие: одна и та же собранная `.dll` работает на Windows и Linux —
машинный код генерируется на месте. Именно поэтому наш проект собирается на Windows, а
может выполняться на Linux-сервере.

CLR даёт ещё две вещи, о которых стоит знать:

- **Сборщик мусора** (Garbage Collector, GC) — автоматически освобождает память объектов,
  на которые больше никто не ссылается. В C# нет `free()` или `delete`.
- **Проверка типов** во время выполнения — нельзя случайно интерпретировать одну структуру
  памяти как другую.

### SDK и Runtime

**SDK** (Software Development Kit) — всё для разработки: компилятор, команда `dotnet`,
шаблоны. **Runtime** — только то, что нужно, чтобы запустить готовое приложение. На своей
машине у тебя SDK; на сервер достаточно поставить Runtime.

### Целевая платформа

В каждом `.csproj` есть строка:

```xml
<TargetFramework>net10.0</TargetFramework>
```

Это версия .NET, под которую собирается проект: какие библиотеки доступны, какие возможности
языка разрешены. У всех четырёх наших проектов `net10.0` — они собираются одной версией.

---

## Часть 2. Анатомия файла

Возьмём [`IMetricsCollector.cs`](../../ServerMonitor.Infrastructure/Monitoring/IMetricsCollector.cs)
целиком — восемь строк, а в них три языковые конструкции:

```csharp
using ServerMonitor.Domain.Entities;

namespace ServerMonitor.Infrastructure.Monitoring;

public interface IMetricsCollector
{
    Task<MetricSnapshot> CollectAsync(CancellationToken cancellationToken = default);
}
```

### `using` — импорт пространства имён

Строка `using ServerMonitor.Domain.Entities;` говорит: «в этом файле имя `MetricSnapshot`
можно писать коротко». Без неё пришлось бы каждый раз писать полное имя
`ServerMonitor.Domain.Entities.MetricSnapshot`.

`using` **не подключает библиотеку** — библиотеки подключаются в `.csproj` через
`<PackageReference>` и `<ProjectReference>`. `using` только сокращает запись.

### `namespace` с точкой с запятой

```csharp
namespace ServerMonitor.Infrastructure.Monitoring;
```

Это **файловое пространство имён** (file-scoped namespace) — форма записи, появившаяся в
C# 10. Раньше писали с фигурными скобками, и весь файл получал лишний уровень отступа:

```csharp
namespace ServerMonitor.Infrastructure.Monitoring
{
    public interface IMetricsCollector { ... }
}
```

Обе формы равнозначны, но в одном файле может быть только одна файловая декларация.

### Куда делись `using System;` и прочие

В `.csproj` всех наших проектов есть:

```xml
<ImplicitUsings>enable</ImplicitUsings>
```

Это **неявные using**: компилятор сам добавляет в каждый файл набор стандартных импортов —
`System`, `System.Collections.Generic`, `System.Linq`, `System.Threading.Tasks` и другие
(для веб-проектов список шире). Поэтому `Task`, `List<T>`, `DateTime` и LINQ-методы
доступны без единой строчки `using`.

Список подставленных импортов можно увидеть глазами — файл
`obj/Debug/net10.0/ServerMonitor.Web.GlobalUsings.g.cs`, буква `g` в имени означает
«generated», сгенерировано.

---

## Часть 3. Классы, объекты и их члены

### Класс и объект

**Класс** — описание, чертёж. **Объект** (экземпляр, instance) — конкретная вещь, созданная
по чертежу оператором `new`. `MetricSnapshot` — класс; каждый замер раз в 5 секунд — новый
объект этого класса.

### Свойства против полей

В [`MetricSnapshot`](../../SeverMonitor.Domain/Entities/MetricSnapshot.cs):

```csharp
public double CpuUsagePercent { get; set; }
```

Это **автосвойство** (auto-property). Свойство — это пара методов: «получить» (`get`) и
«задать» (`set`), которые снаружи выглядят как переменная. Компилятор сам создаёт скрытое
поле для хранения и два метода доступа.

Поле выглядело бы так:

```csharp
public double cpuUsagePercent;   // так в нашем коде не делают
```

> **Почему свойства, а не поля.** Во-первых, свойство можно позже изменить, не ломая код
> вокруг: добавить проверку в `set`, логирование, вычисление — внешний код продолжит писать
> `obj.CpuUsagePercent = 5`. С полем такой фокус не пройдёт, придётся править всех.
> Во-вторых, многие библиотеки (EF Core, JSON-сериализатор, Blazor) по умолчанию работают
> именно со свойствами. Публичное поле в C# — почти всегда ошибка стиля.

Настоящие поля в проекте тоже есть — приватные, для внутреннего состояния:

```csharp
// TelegramBotService.cs
private readonly IConfiguration _configuration;
private bool _cpuWasHigh = false;
```

Соглашение об именах: приватные поля — с подчёркиванием и с маленькой буквы (`_configuration`),
публичные члены — с большой (`CpuUsagePercent`). Компилятор это не проверяет, но так пишут
все, и на code review за нарушение сделают замечание.

### `readonly`

```csharp
private readonly AppDbContext _dbContext;
```

`readonly` означает: значение можно присвоить только при объявлении или в конструкторе,
дальше — нельзя. Это защита от случайной подмены зависимости в середине работы объекта.
Хорошая привычка: все внедрённые зависимости помечать `readonly`.

### Конструктор

```csharp
public MetricsController(AppDbContext dbContext)
{
    _dbContext = dbContext;
}
```

Метод без типа возвращаемого значения, имя совпадает с именем класса. Вызывается при
создании объекта. Здесь он получает `AppDbContext` снаружи и запоминает — это **внедрение
зависимости через конструктор** (constructor injection); механизм, который передаёт сюда
объект, разбирается в главе 02.

### Вычисляемые свойства

В [`PagedResult<T>`](../../ServerMonitor.Api/Dtos/PagedResult.cs):

```csharp
public bool HasPrevious => Page > 1;
public bool HasNext => Page < TotalPages;
```

Здесь нет хранилища — значение вычисляется каждый раз при обращении. Стрелка `=>` в этом
контексте называется **выражение-тело** (expression body) и означает «свойство только для
чтения, возвращающее это выражение». Полная форма была бы:

```csharp
public bool HasPrevious
{
    get { return Page > 1; }
}
```

То же самое `=>` используется и для методов:

```csharp
// Dashboard.razor
private void TogglePause() => isPaused = !isPaused;
public void Dispose() => refreshTimer?.Dispose();
```

### Инициализатор объекта

```csharp
var snapshot = new MetricSnapshot
{
    TimestampUtc = DateTime.UtcNow
};
```

Фигурные скобки после `new` — **инициализатор объекта**: создать объект и сразу присвоить
свойства. Эквивалент трёх строк (создать, потом присвоить), но компактнее. В нашем коде так
собираются все DTO.

### `static`

```csharp
private static string FormatUpTime(double totalSeconds) { ... }
```

`static` — метод принадлежит **классу, а не объекту**. Ему не нужны данные конкретного
экземпляра, поэтому он не может обращаться к `_dbContext` и другим полям объекта. Вызывается
как `MetricsController.FormatUpTime(...)` (внутри класса — просто по имени).

Правило: если метод не использует состояние объекта, делай его `static` — это и подсказка
читателю, и микроскопический выигрыш в производительности.

---

## Часть 4. Типы значимые и ссылочные

Это одно из фундаментальных различий в .NET, и его любят спрашивать.

**Значимые типы** (value types) хранят само значение: `int`, `double`, `bool`, `DateTime`,
`TimeSpan`, любые `struct` и `enum`. При присваивании копируются целиком.

**Ссылочные типы** (reference types) хранят ссылку на объект: `string`, массивы, `List<T>`,
все `class`. При присваивании копируется ссылка, а объект остаётся один.

```csharp
var a = new MetricSnapshot { CpuUsagePercent = 10 };
var b = a;                      // b и a указывают на ОДИН объект
b.CpuUsagePercent = 99;         // a.CpuUsagePercent теперь тоже 99

double x = 10;
double y = x;                   // y — независимая копия
y = 99;                         // x остался 10
```

В нашем коде есть один самодельный значимый тип — в
[`MetricsCollector.cs`](../../ServerMonitor.Infrastructure/Monitoring/MetricsCollector.cs):

```csharp
private readonly struct CpuTimes
{
    public long Idle { get; init; }
    public long Total { get; init; }
}
```

Три детали:

- `struct` вместо `class` — маленькая, короткоживущая пачка чисел; значимый тип не создаёт
  нагрузку на сборщик мусора.
- `readonly struct` — гарантия, что содержимое не изменится после создания.
- `init` вместо `set` — свойство можно задать **только в момент создания** объекта
  (в инициализаторе), дальше оно неизменяемо.

### Типы, которые встречаются в проекте

| Тип | Что это | Где у нас |
|-----|---------|-----------|
| `int` | целое, 32 бита | `Id`, `Page`, `PageSize` |
| `long` | целое, 64 бита | счётчики CPU в `CpuTimes` |
| `double` | дробное с плавающей точкой | все метрики |
| `bool` | истина/ложь | `AlertsEnabled`, `isPaused` |
| `string` | текст | `MetricType`, `Uptime` |
| `DateTime` | момент времени | `TimestampUtc` |
| `TimeSpan` | длительность | интервалы, `TimeSpan.FromSeconds(5)` |

Про `DateTime` отдельно: у него есть свойство `Kind` — «это местное время или UTC?».
Мы везде храним UTC (`DateTime.UtcNow`) и переводим в местное только при показе
(`.ToLocalTime()` в разметке страниц). Это правильная практика: сервер может стоять в другом
часовом поясе, а пользователи — в третьем; единственная надёжная точка отсчёта — UTC.

---

## Часть 5. `null` и как язык от него защищает

`null` — «ссылка в никуда». Обращение к члену такой ссылки роняет программу с
`NullReferenceException` — исторически самая частая ошибка в C#.

В `.csproj` каждого проекта включена защита:

```xml
<Nullable>enable</Nullable>
```

В этом режиме компилятор считает, что обычный `string` **не может** быть `null`, а если
может — это нужно объявить явно через `?`:

```csharp
private string? _chatId;                  // может быть null — компилятор знает
private readonly HttpClient _httpClient;  // не может быть null
```

Отсюда конструкция, которая иначе выглядит странно:

```csharp
public string MetricType { get; set; } = string.Empty;
```

Свойство типа `string` (не `string?`), поэтому его нужно чем-то инициализировать — иначе
компилятор предупредит: «объект может остаться без значения». `string.Empty` — пустая
строка, безопасное значение по умолчанию.

### Операторы для работы с null

| Оператор | Что делает | Пример из кода |
|----------|-----------|----------------|
| `?.` | обратиться, только если не null | `refreshTimer?.Dispose()` |
| `??` | значение слева, а если null — справа | `items ?? new List<MetricHistoryItem>()` |
| `??=` | присвоить, только если сейчас null | в проекте не используется |
| `is null` | проверка на null | `if (latest is null) return NotFound(...)` |

Разбор реального примера из [`MetricsApiClient`](../../ServerMonitor.Web/Services/MetricsApiClient.cs):

```csharp
var items = await _httpClient.GetFromJsonAsync<List<MetricHistoryItem>>(url, cancellationToken);
return items ?? new List<MetricHistoryItem>();
```

`GetFromJsonAsync` возвращает `List<...>?` — сервер мог прислать литерал `null`. Вместо того
чтобы отдать `null` наружу и заставить каждую страницу проверять, метод подставляет пустой
список. Вызывающий код может спокойно писать `foreach` — цикл по пустому списку просто не
сделает ни одной итерации. Это хороший приём: **не отдавай наружу null там, где можно
отдать пустую коллекцию**.

И `?.` из [`Dashboard.razor`](../../ServerMonitor.Web/Components/Pages/Dashboard.razor):

```csharp
public void Dispose() => refreshTimer?.Dispose();
```

Таймер мог не создаться (например, страница закрылась до инициализации). `?.` означает:
если `refreshTimer` — null, ничего не делать; иначе вызвать `Dispose()`.

### `is null` вместо `== null`

В проекте везде `is null`. Разница тонкая: `==` может быть переопределён классом и повести
себя неожиданно, а `is null` — языковая конструкция, которую подменить нельзя. Считается
более надёжной формой.

---

## Часть 6. `var`

```csharp
var latest = await _dbContext.MetricSnapshots...FirstOrDefaultAsync(cancellationToken);
```

`var` — «выведи тип сам». Это **не** динамическая типизация: тип определяется на этапе
компиляции и дальше строго фиксирован. Здесь `latest` — это `MetricSnapshot?`, просто не
написано руками.

Используют `var`, когда тип очевиден из правой части или слишком длинный
(`Dictionary<string, List<MetricSnapshot>>`). Когда тип неочевиден — лучше написать явно.

---

## Часть 7. Интерфейсы

[`IMetricsCollector`](../../ServerMonitor.Infrastructure/Monitoring/IMetricsCollector.cs) —
это **контракт**: перечень того, что умеет делать объект, без единой строчки реализации.

```csharp
public interface IMetricsCollector
{
    Task<MetricSnapshot> CollectAsync(CancellationToken cancellationToken = default);
}
```

Класс [`MetricsCollector`](../../ServerMonitor.Infrastructure/Monitoring/MetricsCollector.cs)
обещает выполнить контракт:

```csharp
public class MetricsCollector : IMetricsCollector
```

Буква `I` в начале имени — соглашение .NET (в Java, например, так не делают).

> **Почему интерфейс, а не сразу класс.** Смотри на
> [`MetricsCollectorService`](../../ServerMonitor.Infrastructure/Monitoring/MetricsCollectorService.cs):
> он принимает `IMetricsCollector`, а не `MetricsCollector`. Значит, фоновый сервис не знает
> и не хочет знать, откуда берутся числа: из `/proc`, из Windows API или из подделки для
> теста. Это позволяет (а) написать тест, подсунув фальшивый коллектор, который возвращает
> заранее известные значения, и (б) добавить новую реализацию, не трогая сервис. Такой приём
> называется **инверсией зависимостей** (Dependency Inversion) — код зависит от абстракции,
> а не от конкретики.

Параметр `CancellationToken cancellationToken = default` — **значение по умолчанию**: метод
можно вызвать без аргумента, тогда подставится `default` (для `CancellationToken` — «токен,
который никогда не отменяется»).

---

## Часть 8. Обобщения (generics)

`PagedResult<T>` — класс с «дыркой» вместо типа:

```csharp
public class PagedResult<T>
{
    public List<T> Items { get; set; } = new();
    public int Page { get; set; }
    ...
}
```

`T` — параметр типа. При использовании он заменяется конкретным типом:

```csharp
PagedResult<MetricHistoryItemDto>   // страница с элементами истории
PagedResult<AlertDto>               // страница с алертами — тот же класс
```

Без обобщений пришлось бы писать `PagedResultOfMetrics`, `PagedResultOfAlerts` и так далее —
один и тот же код с разными типами. Обобщения дают **повторное использование без потери
типобезопасности**: компилятор знает, что в `PagedResult<MetricHistoryItemDto>.Items` лежат
именно `MetricHistoryItemDto`, и не даст положить туда алерт.

Обобщения в проекте повсюду: `Task<MetricSnapshot>`, `List<Alert>`, `DbSet<MetricSnapshot>`,
`ActionResult<ServerStatusDto>`, `ILogger<TelegramBotService>`.

Мелочь из того же файла:

```csharp
public List<T> Items { get; set; } = new();
```

`new()` без имени типа — **целевая типизация** (target-typed new): компилятор видит слева
`List<T>` и понимает, что создавать. Короткая форма от `new List<T>()`.

---

## Часть 9. Асинхронность: `async`, `await`, `Task`

Самая важная часть главы. Асинхронность пронизывает весь проект, и её почти всегда спрашивают
на собеседовании.

### Проблема, которую решает async

Веб-сервер обслуживает запросы **потоками** (threads) из ограниченного набора — пула
потоков. Поток — дорогой ресурс (память под стек, переключение контекста).

Запрос к базе данных выглядит так: отправили SQL по сети → **ждём** → получили ответ.
Ожидание может занять миллисекунды или секунды, и всё это время процессор не занят: он ждёт
сеть. Если поток просто стоит и ждёт, он занят впустую. Сто одновременных запросов — сто
заблокированных потоков, и сервер задыхается, хотя процессор простаивает.

Асинхронность решает это так: на время ожидания поток **возвращается в пул** и обслуживает
другие запросы. Когда ответ от базы придёт, выполнение продолжится — возможно, уже на другом
потоке.

### `Task` — обещание результата

```csharp
Task<MetricSnapshot> CollectAsync(CancellationToken cancellationToken = default);
```

`Task<MetricSnapshot>` — это **не** сам снимок метрик. Это объект-обещание: «операция
запущена; когда-нибудь здесь будет `MetricSnapshot`, либо исключение». Аналог `Promise` в
JavaScript.

- `Task` — обещание «сделаю» без результата (аналог `void`).
- `Task<T>` — обещание «сделаю и верну `T`».

### `await` — «подожди, не блокируя поток»

```csharp
var snapshot = await _collector.CollectAsync(stoppingToken);
```

`await` разворачивает `Task<MetricSnapshot>` в `MetricSnapshot`. Механика под капотом:

1. Метод доходит до `await` и вызывает `CollectAsync`.
2. Если результат ещё не готов, метод **приостанавливается** и возвращает управление наверх.
   Поток свободен и уходит делать другую работу.
3. Компилятор заранее превратил остаток метода в **продолжение** (continuation) — по сути в
   конечный автомат. Когда `Task` завершится, продолжение будет запущено, и метод пойдёт
   дальше с того же места, с сохранёнными локальными переменными.

Ключевая мысль: `await` **не создаёт поток** и **не ускоряет** отдельную операцию. Он
освобождает поток на время ожидания. Асинхронность — про пропускную способность, а не про
скорость.

### `async` — разрешение использовать `await`

```csharp
public async Task<ActionResult<ServerStatusDto>> GetStatus(CancellationToken cancellationToken)
```

`async` в сигнатуре говорит компилятору: «внутри есть `await`, преврати метод в автомат».
Само по себе `async` ничего не делает — это модификатор компиляции.

Суффикс `Async` в имени (`CollectAsync`, `GetStatusAsync`) — просто соглашение, чтобы по
имени было видно, что метод возвращает `Task`.

### Правило: async насквозь

Если метод асинхронный, вызывающий его тоже должен быть асинхронным. Асинхронность
«поднимается» до самого верха — до контроллера или обработчика события. В нашем коде видно
всю цепочку:

```
Dashboard.OnInitializedAsync
  → MetricsApiClient.GetStatusAsync
      → HttpClient.GetFromJsonAsync   (ожидание сети)
```

и на сервере:

```
MetricsController.GetStatus
  → FirstOrDefaultAsync              (ожидание базы данных)
```

Чего делать **нельзя** — превращать асинхронное в синхронное через `.Result` или `.Wait()`:
в веб-приложении это классический способ получить взаимоблокировку (deadlock) и исчерпание
пула потоков.

### `async void` — единственное исключение и наша ловушка

В [`TopNav.razor`](../../ServerMonitor.Web/Components/Layout/TopNav.razor) есть:

```csharp
private async void OnLocationChanged(object? sender, EventArgs e)
{
    await JS.InvokeVoidAsync("navPill.update");
    await InvokeAsync(StateHasChanged);
}
```

`async void` вместо `async Task` — редкий и опасный случай. Опасность в том, что у `void`
нет `Task`, а значит **некому поймать исключение**: если внутри что-то упадёт, ошибка не
всплывёт к вызывающему коду, а прилетит прямо в среду выполнения и может уронить процесс.

Почему здесь так: метод подписан на событие `Nav.LocationChanged`, а сигнатура обработчика
события в .NET требует именно `void`. Это единственный законный случай для `async void` —
обработчики событий. Правило хорошего тона в такой ситуации: заворачивать тело в `try/catch`,
чтобы исключение не ушло в пустоту.

> **Слабое место.** В нашем обработчике `try/catch` нет. Если JS-вызов упадёт (например,
> элемент ещё не отрисован), исключение окажется необработанным. На практике не стреляло, но
> знать стоит.

### `CancellationToken` — вежливая отмена

Через весь проект тянется параметр `CancellationToken`:

```csharp
public async Task<ActionResult<ServerStatusDto>> GetStatus(CancellationToken cancellationToken)
{
    var latest = await _dbContext.MetricSnapshots
        .OrderByDescending(m => m.TimestampUtc)
        .FirstOrDefaultAsync(cancellationToken);
```

Это **сигнал отмены**. ASP.NET Core сам создаёт токен на каждый запрос и «дёргает» его, если
клиент закрыл вкладку, не дождавшись ответа. Передав токен дальше в базу, мы говорим: «не
нужно больше ждать, запрос никому не нужен» — и освобождаем ресурсы.

У фоновых сервисов роль другая: там токен `stoppingToken` срабатывает при остановке
приложения, чтобы бесконечный цикл корректно завершился, а не был убит на середине.

Обрати внимание на такую конструкцию в
[`MetricsCollectorService`](../../ServerMonitor.Infrastructure/Monitoring/MetricsCollectorService.cs):

```csharp
try
{
    await Task.Delay(_interval, stoppingToken);
}
catch (TaskCanceledException)
{
    break;
}
```

`Task.Delay` с токеном при отмене не досыпает, а **бросает исключение** — так устроен
контракт отмены в .NET. Поэтому его ловят и аккуратно выходят из цикла.

---

## Часть 10. Делегаты и лямбды

**Делегат** — переменная, которая хранит не число и не строку, а **метод**. Возможность
передать поведение как аргумент.

Готовые делегаты из стандартной библиотеки:

- `Func<TResult>` — метод, который что-то возвращает.
- `Action<T>` — метод, который что-то принимает и ничего не возвращает.

Отличный живой пример — [`TelegramBotService.CheckThreshold`](../../ServerMonitor.Infrastructure/Telegram/TelegramBotService.cs):

```csharp
private async Task CheckThreshold(AppDbContext dbContext, double value, double threshold, string name,
    Func<bool> getWasHigh, Action<bool> setWasHigh, CancellationToken cancellationToken)
```

и вызовы:

```csharp
await CheckThreshold(dbContext, cpu, settings.CpuThreshold, "CPU",
    () => _cpuWasHigh, v => _cpuWasHigh = v, cancellationToken);
await CheckThreshold(dbContext, memory, settings.MemoryThreshold, "Memory",
    () => _memoryWasHigh, v => _memoryWasHigh = v, cancellationToken);
```

Что здесь происходит. Логика проверки порога одинакова для CPU, памяти и диска, но каждая
метрика хранит своё состояние в отдельном поле (`_cpuWasHigh`, `_memoryWasHigh`,
`_diskWasHigh`). Передать поле «по ссылке» в асинхронный метод язык не позволяет. Поэтому
передаются два маленьких метода: «прочитать это поле» и «записать в это поле».

- `() => _cpuWasHigh` — лямбда без параметров, возвращает значение поля. Это `Func<bool>`.
- `v => _cpuWasHigh = v` — лямбда с параметром `v`, присваивает полю. Это `Action<bool>`.

**Лямбда** (lambda expression) — способ объявить метод прямо на месте, без имени. Синтаксис:
`параметры => тело`. Если параметр один, скобки можно опустить: `v => ...`.

Такая лямбда **захватывает** (capture) окружение — в данном случае ссылку на объект
`TelegramBotService`, чтобы добраться до его полей. Компилятор для этого создаёт скрытый
класс; поэтому лямбды — не бесплатная конструкция, хотя в подавляющем большинстве случаев
об этом можно не думать.

> **Стоит знать.** Решение с `Func`/`Action` рабочее, но не самое чистое. Естественнее было
> бы хранить состояние в словаре `Dictionary<string, bool>` по имени метрики или завести
> маленький класс «состояние метрики». Тогда сигнатура стала бы короче и понятнее. Это
> хороший кандидат на рефакторинг при доработке алертинга.

Лямбды в проекте встречаются и проще — в LINQ и в таймере:

```csharp
refreshTimer = new Timer(async _ => { ... }, null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
```

Здесь `_` — общепринятое имя для параметра, который не используется.

---

## Часть 11. LINQ

**LINQ** (Language Integrated Query) — единый синтаксис запросов к данным: к спискам в
памяти, к базе данных, к XML. В проекте используется «методный» синтаксис — цепочка вызовов.

Разберём запрос из [`MetricsController`](../../ServerMonitor.Api/Controllers/MetricsControllet.cs):

```csharp
var items = await _dbContext.MetricSnapshots
    .OrderByDescending(m => m.TimestampUtc)
    .Take(count)
    .Select(m => new MetricHistoryItemDto
    {
        TimestampUtc = m.TimestampUtc,
        CpuUsagePercent = m.CpuUsagePercent,
        ...
    })
    .ToListAsync(cancellationToken);
```

По шагам:

| Метод | Что делает |
|-------|-----------|
| `OrderByDescending(m => m.TimestampUtc)` | сортировка по убыванию времени |
| `Take(count)` | взять первые `count` элементов |
| `Select(m => new ...)` | преобразовать каждый элемент в другой тип |
| `ToListAsync(...)` | **выполнить** запрос и собрать результат в список |

Лямбда `m => m.TimestampUtc` читается «для каждого элемента `m` взять его `TimestampUtc`».
Имя `m` произвольно.

### Отложенное выполнение — главное о LINQ

Пока ты вызываешь `OrderByDescending`, `Take`, `Select`, **никакой работы не происходит**.
Строится описание запроса. Реальное выполнение запускает только «материализующий» вызов:
`ToListAsync`, `FirstOrDefaultAsync`, `CountAsync`, `AverageAsync`.

Для базы данных это принципиально: EF Core берёт всё построенное описание и переводит его в
**один SQL-запрос**. Приведённый выше код превращается примерно в:

```sql
SELECT "TimestampUtc", "CpuUsagePercent", ...
FROM "MetricSnapshots"
ORDER BY "TimestampUtc" DESC
LIMIT 50
```

Обрати внимание: `Select` тоже уехал в SQL — из базы приедут только нужные колонки, а не
целые строки. Отсюда важное следствие: если бы мы сначала вызвали `ToListAsync()`, а потом
сортировали, база отдала бы **всю таблицу**, и сортировка пошла бы в памяти приложения. На
десятках тысяч строк это разница между миллисекундами и секундами.

Красивый пример пошагового построения — в методе `GetHistoryPaged`:

```csharp
IQueryable<MetricSnapshot> query = _dbContext.MetricSnapshots;

if (from.HasValue)
{
    var fromUtc = DateTime.SpecifyKind(from.Value, DateTimeKind.Utc);
    query = query.Where(m => m.TimestampUtc >= fromUtc);
}
```

Запрос собирается по кусочкам в зависимости от того, какие фильтры прислал пользователь, и
только в конце выполняется. Все условия попадут в одну SQL-команду. Разница между
`IQueryable` (запрос, который ещё превратится в SQL) и `IEnumerable` (данные уже в памяти)
подробно разбирается в главе 03 — это одно из самых частых мест, где начинающие теряют
производительность.

### LINQ по спискам в памяти

Те же методы работают и без базы. В `Dashboard.razor`:

```csharp
var avg = history.Average(selector);
var max = history.Max(selector);
```

`history` — обычный `List<MetricHistoryItem>`, никакого SQL, всё считается на месте.

---

## Часть 12. Конструкции языка, встречающиеся в коде

### Switch-выражение

```csharp
// MetricCard.razor
private string BarColor => Percent switch
{
    >= 90 => "#ef4444",
    >= 70 => "#f59e0b",
    _ => "#10b981"
};
```

Это **switch-выражение** (switch expression): в отличие от классического `switch`, оно
**возвращает значение**, а не выполняет ветки. `_` — «всё остальное» (аналог `default`).
Условия `>= 90` — это шаблоны сравнения, проверяются сверху вниз, первый подошедший
выигрывает.

Более сложный пример — выбор сортировки в контроллере:

```csharp
query = sortBy.ToLower() switch
{
    "cpu" => ascending ? query.OrderBy(m => m.CpuUsagePercent)
                       : query.OrderByDescending(m => m.CpuUsagePercent),
    ...
    _ => ascending ? query.OrderBy(m => m.TimestampUtc)
                   : query.OrderByDescending(m => m.TimestampUtc)
};
```

Здесь внутри каждой ветки ещё и **тернарный оператор** `условие ? если_да : если_нет`.

### Сопоставление с шаблоном

Самая замысловатая строка проекта — в обработчике команд Telegram:

```csharp
if (update.Message is not { Text: { } messageText } message)
    return;
```

Читается так: «если `update.Message` **не** является объектом, у которого свойство `Text`
не равно null — выйти». Заодно, если проверка прошла, объявляются две переменные:
`messageText` (текст сообщения) и `message` (само сообщение).

`{ }` внутри означает «любой не-null объект». То есть `Text: { }` — «`Text` существует и не
null». Развёрнутая запись того же самого:

```csharp
if (update.Message == null || update.Message.Text == null)
    return;
var message = update.Message;
var messageText = update.Message.Text;
```

Компактность здесь спорна: четыре понятные строки против одной загадочной. В реальном коде
такие шаблоны лучше применять там, где они действительно упрощают чтение.

### Интерполяция строк

```csharp
return $"{(int)span.TotalDays}d {span.Hours}h {span.Minutes}m";
```

Знак `$` перед строкой разрешает вставлять выражения в фигурных скобках. `(int)` — приведение
типа: отбрасывает дробную часть у `double`.

Форматирование через двоеточие:

```csharp
$"{value:F1}%"                     // 4.6  — один знак после запятой
$"{status.MemoryUsedMb:F0} MB"     // 8192 — без дробной части
$"{latest.TimestampUtc:HH:mm:ss}"  // 19:32:41
```

> **Важная ловушка, на которую мы уже наступили.** Форматирование чисел зависит от
> **культуры** (языковых настроек) системы. В русской локали `4.63.ToString("F2")` даст
> `"4,63"` — с запятой. Для показа человеку это правильно, но если такая строка попадает в
> CSS или SVG, браузер её не поймёт: там разделитель обязан быть точкой. Именно этот баг был
> в прогресс-барах — ширина `width: 4,63%` игнорировалась, и полоса всегда рисовалась во всю
> длину. Лечение — явно указывать инвариантную культуру:
> ```csharp
> Percent.Value.ToString("0.##", CultureInfo.InvariantCulture)
> ```
> Правило: **всё, что читает машина** (CSS, JSON, SQL, URL), форматируй через
> `CultureInfo.InvariantCulture`; культуру пользователя применяй только к тому, что видит
> человек.

### `try` / `catch`

```csharp
try
{
    var snapshot = await _collector.CollectAsync(stoppingToken);
    ...
}
catch (Exception ex)
{
    _logger.LogError(ex, "Error while collecting metrics.");
}
```

Смысл именно этого блока: фоновый сервис работает вечно, и одна неудачная попытка (база
недоступна, файл `/proc` не прочитался) **не должна убивать цикл**. Ошибка записывается в
журнал, цикл идёт дальше и попробует снова через 5 секунд.

Ловить `Exception` (самый общий тип) в обычном коде — плохая практика: так можно проглотить
ошибку, о которой надо было узнать. Но в фоновом цикле, где важна живучесть, это оправданно —
при условии, что ошибка **логируется**, а не игнорируется молча.

Пример молчаливого проглатывания в проекте всё же есть, в `MetricsCollector`:

```csharp
try
{
    total += process.TotalProcessorTime;
}
catch
{

}
```

Здесь у некоторых системных процессов нет прав на чтение времени — исключение ожидаемо, и
его сознательно игнорируют. Приемлемо, но комментарий с объяснением сильно помог бы
читателю (и лучше ловить конкретный тип исключения, а не все подряд).

### `using var`

```csharp
using var scope = _scopeFactory.CreateScope();
```

**Объявление using** (using declaration): когда переменная выйдет из области видимости
(конец метода), у объекта автоматически вызовется `Dispose()` — освобождение ресурсов. Это
гарантируется даже при исключении.

Не путать с `using` вверху файла — совпадает только слово. Здесь речь про управление
временем жизни объекта; зачем это нужно именно фоновым сервисам — в главе 04.

### Атрибуты

```csharp
[ApiController]
[Route("api/[controller]")]
public class MetricsController : ControllerBase
```

**Атрибут** — метаданные, прикреплённые к классу, методу или параметру. Сами по себе они
ничего не делают: их читает кто-то другой (фреймворк) во время работы через **рефлексию**
(механизм чтения информации о типах). ASP.NET Core видит `[HttpGet("status")]` и понимает,
на какой URL направить метод.

Атрибуты в проекте: `[ApiController]`, `[Route]`, `[HttpGet]`, `[HttpPut]`, `[FromQuery]`,
`[FromBody]` (глава 02), `[Parameter]` в Blazor-компонентах (глава 06), `[DllImport]` и
`[StructLayout]` для вызова Windows API (глава 05).

---

## Что запомнить из главы

- C# компилируется в промежуточный код IL, а машинный код создаётся при запуске (JIT) —
  поэтому одна сборка работает на разных ОС.
- Свойства (`{ get; set; }`) — не поля; библиотеки работают именно со свойствами.
- Значимые типы копируются по значению, ссылочные — по ссылке.
- `<Nullable>enable</Nullable>` заставляет объявлять «может быть null» явно через `?`.
- `await` не создаёт поток и не ускоряет операцию — он освобождает поток на время ожидания.
- `async void` допустим только в обработчиках событий, и то с `try/catch`.
- LINQ выполняется отложенно; к базе уходит один SQL-запрос, собранный из всей цепочки.
- Числа для машины форматируй через `CultureInfo.InvariantCulture` — иначе запятая вместо
  точки ломает CSS, JSON и SQL.

Дальше: глава 02 — как ASP.NET Core превращает HTTP-запрос в вызов метода контроллера, что
такое DI-контейнер и зачем нужны DTO.
