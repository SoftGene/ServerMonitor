# Глава 05 — Как снимаются метрики

Единственное место проекта, где код разговаривает не с базой и не с браузером, а напрямую с
операционной системой. Здесь два совершенно разных подхода — файлы в Linux и системные вызовы
в Windows — и одна нетривиальная идея: загрузку процессора невозможно измерить мгновенно.

Разбор идёт по двум файлам:

- [`MetricsCollector.cs`](../../ServerMonitor.Collection/MetricsCollector.cs) —
  обращения к операционной системе: чтение файлов, вызовы Windows API, работа с дисками;
- [`ProcParser.cs`](../../ServerMonitor.Collection/ProcParser.cs) — **чистые
  функции** разбора текста и вычисления загрузки, без единого обращения к файловой системе.

Такое разделение появилось не сразу: сначала разбор был вперемешку с чтением файлов, и
покрыть его тестами было невозможно — для этого понадобился бы настоящий Linux. Вынесли
разбор в отдельный класс, куда на вход подаётся уже прочитанный текст, — и тесты стали
писаться в одну строчку (глава 09).

---

## Часть 1. Развилка по операционной системе

```csharp
public async Task<MetricSnapshot> CollectAsync(CancellationToken cancellationToken = default)
{
    var snapshot = new MetricSnapshot { TimestampUtc = DateTime.UtcNow };

    if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
    {
        await CollectLinuxMetricsAsync(snapshot, cancellationToken);
    }
    else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
    {
        await CollectWindowsMetricsAsync(snapshot, cancellationToken);
    }
    else
    {
        throw new PlatformNotSupportedException("Only Linux and Windows are supported.");
    }

    CollectDiskMetrics(snapshot);

    return snapshot;
}
```

`RuntimeInformation.IsOSPlatform(...)` из пространства имён `System.Runtime.InteropServices`
отвечает на вопрос «на чём мы сейчас работаем». Проверка происходит **во время выполнения**,
а не при компиляции: одна и та же сборка работает и там, и там (глава 01, про IL и JIT).

Структура метода — образцовая:

- метка времени ставится **сразу**, до всех измерений: она относится к моменту начала замера;
- платформенно-зависимая часть вынесена в отдельные методы;
- **диск читается одинаково для обеих систем** — .NET уже дал кроссплатформенный API;
- на macOS честно бросается исключение, а не возвращается пустой снимок с нулями. Это верное
  решение: тихий ноль в мониторинге хуже громкой ошибки, потому что выглядит как настоящее
  измерение.

---

## Часть 2. Linux: файловая система `/proc`

### Что такое `/proc`

В Linux ядро отдаёт информацию о себе через **виртуальную файловую систему** `/proc`. Это не
файлы на диске: при чтении ядро формирует содержимое на лету. Зато работать с ними можно
обычными файловыми функциями — что наш код и делает.

Три файла, которые нам нужны:

| Файл | Что содержит |
|------|--------------|
| `/proc/meminfo` | таблица «параметр: значение kB» про память |
| `/proc/uptime` | два числа: секунды с загрузки и суммарный простой |
| `/proc/stat` | накопленные счётчики времени процессора |

### Память

```csharp
var lines = await File.ReadAllLinesAsync("/proc/meminfo", cancellationToken);

double totalKb = 0;
double availableKb = 0;

foreach (var line in lines)
{
    if (line.StartsWith("MemTotal:"))          totalKb = ParseMemInfoLine(line);
    else if (line.StartsWith("MemAvailable:")) availableKb = ParseMemInfoLine(line);
}

var usedKb = totalKb - availableKb;

snapshot.MemoryTotalMb = totalKb / 1024.0;
snapshot.MemoryUsedMb = usedKb / 1024.0;
```

Файл выглядит примерно так:

```
MemTotal:       16316412 kB
MemFree:          264132 kB
MemAvailable:   11238904 kB
Buffers:          198456 kB
Cached:          9871232 kB
```

Разбор строки:

```csharp
private static double ParseMemInfoLine(string line)
{
    var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    return double.Parse(parts[1]);
}
```

`StringSplitOptions.RemoveEmptyEntries` здесь обязателен: между двоеточием и числом стоит
много пробелов, и без этого флага в массиве оказались бы пустые строки. С флагом получаем
`["MemTotal:", "16316412", "kB"]`, и нужное значение — под индексом 1.

> **Почему `MemAvailable`, а не `MemFree`.** Это ключевое решение всего блока. `MemFree` —
> память, которую ядро вообще ни на что не потратило; на здоровой Linux-системе она всегда
> близка к нулю, потому что свободную память ядро отдаёт под дисковый кэш. Если считать по
> `MemFree`, дашборд будет вечно показывать «занято 98 %» и пугать. `MemAvailable` — оценка
> ядра «сколько можно выделить новому приложению без свопа», с учётом того, что кэш при
> необходимости освободится. Именно это число соответствует интуитивному «свободно».

### Время работы

```csharp
var content = await File.ReadAllTextAsync("/proc/uptime", cancellationToken);
var parts = content.Split(' ', StringSplitOptions.RemoveEmptyEntries);
snapshot.UptimeSeconds = double.Parse(parts[0]);
```

Файл содержит две дроби, например `348915.42 2712334.11`: секунды с момента загрузки и
суммарное время простоя всех ядер. Нам нужно первое.

> **Здесь была серьёзная ошибка — и её исправили.** Раньше строка выглядела так:
> `double.Parse(parts[0])`, без указания культуры. Такой вызов использует **культуру текущего
> потока**. В `/proc/uptime` дробная часть отделена **точкой** — это машинный формат, он не
> зависит от языка системы. Но если процесс запущен с русской культурой, разделителем
> считается запятая, и строка `"348915.42"` разбором не принимается: `double.Parse` бросает
> `FormatException`.
>
> Баг был латентным: на Windows этот код не выполняется, а у служб на Linux обычно
> инвариантная культура или `en_US`. Но достаточно было запустить сервис с
> `LANG=ru_RU.UTF-8`, и сбор метрик начал бы падать каждые несколько секунд — правда,
> аккуратно логируясь благодаря `try/catch` из главы 04.
>
> Сейчас все разборы чисел в `ProcParser` идут через инвариантную культуру:
> ```csharp
> return double.Parse(parts[0], CultureInfo.InvariantCulture);
> ```
> **Данные от машины разбирай инвариантной культурой.** То же правило мы уже встречали в
> истории с шириной прогресс-баров (глава 01) — там culture-зависимое форматирование ломало
> CSS, здесь ломало бы разбор. На этот случай есть отдельный регрессионный тест, который
> подменяет культуру потока на `ru-RU` и проверяет, что разбор всё равно работает.

### Загрузка процессора: почему нужна пауза

Самая интересная часть главы.

```csharp
private async Task CollectLinuxCpuAsync(MetricSnapshot snapshot, CancellationToken cancellationToken)
{
    var first = await ReadCpuTimesAsync(cancellationToken);

    await Task.Delay(1000, cancellationToken);

    var second = await ReadCpuTimesAsync(cancellationToken);

    var totalDelta = second.Total - first.Total;
    var idleDelta = second.Idle - first.Idle;

    if (totalDelta <= 0)
    {
        snapshot.CpuUsagePercent = 0;
        return;
    }

    var usage = (1.0 - (double)idleDelta / totalDelta) * 100.0;
    snapshot.CpuUsagePercent = Math.Round(usage, 2);
}
```

**Ядро не хранит «текущую загрузку».** В `/proc/stat` лежат счётчики, которые только растут с
момента запуска системы:

```
cpu  2255884 12345 654321 98765432 45678 0 12345 0 0 0
```

Числа — это **такты** (jiffies), проведённые процессором в разных состояниях: пользовательский
код, «вежливые» процессы, системный код, простой, ожидание диска, обработка прерываний.

Прочитав их один раз, мы узнаем только суммарную статистику за всё время работы машины — это
не «загрузка сейчас». Загрузка — это **производная**: сколько тактов утекло в простой за
последнюю секунду по сравнению с общим числом тактов за ту же секунду.

Отсюда алгоритм: замерить — подождать секунду — замерить снова — поделить разницы.

```mermaid
flowchart LR
    T1["Момент t₁<br/>total=100000<br/>idle=95000"] -->|"пауза 1 сек"| T2["Момент t₂<br/>total=100400<br/>idle=95300"]
    T2 --> D["Δtotal = 400<br/>Δidle = 300"]
    D --> R["загрузка = (1 − 300/400) × 100 = 25 %"]
```

Чтение счётчиков:

```csharp
var lines = await File.ReadAllLinesAsync("/proc/stat", cancellationToken);
var cpuLine = lines[0];
var parts = cpuLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);

long user = long.Parse(parts[1]);
long nice = long.Parse(parts[2]);
long system = long.Parse(parts[3]);
long idle = long.Parse(parts[4]);
long iowait = long.Parse(parts[5]);
long irq = long.Parse(parts[6]);
long softirq = long.Parse(parts[7]);

long idleTotal = idle + iowait;
long total = user + nice + system + idle + iowait + irq + softirq;

return new CpuTimes { Idle = idleTotal, Total = total };
```

Берётся `lines[0]` — первая строка `cpu` — это суммарная статистика по всем ядрам (ниже в
файле идут `cpu0`, `cpu1` и так далее по каждому ядру отдельно).

Две содержательные детали:

- **`iowait` учтён как простой.** Процессор ждёт диск — работой это не считается. Спорное, но
  распространённое решение: так же поступает классическая утилита `top`.
- **Проверка `if (totalDelta <= 0)`.** Защита от деления на ноль: если счётчики почему-то не
  изменились (система полностью простаивала на очень грубом таймере), возвращаем 0 вместо
  падения.

Формула `(1.0 - (double)idleDelta / totalDelta) * 100.0` — «доля времени, когда процессор
**не** простаивал». Приведение `(double)` обязательно: без него `idleDelta / totalDelta`
было бы **целочисленным делением** и дало бы 0 при любом idle меньше total, а результат
всегда получался бы ровно 100 %. Классическая ошибка, которую здесь удалось не допустить.

> **Связь с главой 04.** Именно эта секундная пауза делает реальный шаг сбора равным примерно
> шести секундам вместо пяти. Убрать её нельзя — без неё загрузку процессора не измерить.
> Обойти можно: хранить предыдущие показания счётчиков в поле сервиса и считать дельту между
> соседними итерациями цикла. Тогда пауза не нужна, а интервалом станет период сбора.

---

## Часть 3. Windows: вызов системных функций

В Windows нет `/proc`. Данные приходится брать из функций системных библиотек, написанных на
C. Механизм называется **P/Invoke** (Platform Invocation).

### Объявление внешней функции

```csharp
[DllImport("kernel32.dll", SetLastError = true)]
[return: MarshalAs(UnmanagedType.Bool)]
private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);
```

Разбираем построчно:

- **`[DllImport("kernel32.dll")]`** — функция лежит в системной библиотеке `kernel32.dll`.
- **`SetLastError = true`** — сохранять код ошибки Windows, чтобы его можно было получить
  через `Marshal.GetLastWin32Error()`.
- **`extern`** — у метода **нет тела** на C#; реализация во внешней библиотеке.
- **`[return: MarshalAs(UnmanagedType.Bool)]`** — как трактовать возвращаемое значение:
  в C-коде это 4-байтовый `BOOL`, а в C# `bool` — один байт. Атрибут задаёт правило перевода.

Перевод данных между миром .NET и миром C называется **маршалингом** (marshalling).

### Структура с точной раскладкой памяти

```csharp
[StructLayout(LayoutKind.Sequential)]
private struct MEMORYSTATUSEX
{
    public uint dwLength;
    public uint dwMemoryLoad;
    public ulong ullTotalPhys;
    public ulong ullAvailPhys;
    public ulong ullTotalPageFile;
    public ulong ullAvailPageFile;
    public ulong ullTotalVirtual;
    public ulong ullAvailVirtual;
    public ulong ullAvailExtendedVirtual;
}
```

**`[StructLayout(LayoutKind.Sequential)]`** — критически важная строка. По умолчанию среда
выполнения имеет право переставлять поля структуры в памяти как ей удобнее. Функция Windows
ожидает поля строго в объявленном порядке и с точными размерами. Атрибут это гарантирует.

Порядок, имена и типы обязаны один в один соответствовать документации Windows. Ошибка в
одном поле — и функция запишет данные не туда, куда мы думаем; в лучшем случае получим
бессмыслицу, в худшем — повреждение памяти.

Префиксы `dw` и `ull` — это венгерская нотация из мира Win32: `dw` = double word (32 бита),
`ull` = unsigned long long (64 бита). В C# так не пишут, но здесь имена копируются из
документации ради узнаваемости.

### Вызов

```csharp
var memStatus = new MEMORYSTATUSEX
{
    dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>()
};

if (!GlobalMemoryStatusEx(ref memStatus))
{
    return;
}

const double bytesPerMb = 1024.0 * 1024.0;
double totalMb = memStatus.ullTotalPhys / bytesPerMb;
double availMb = memStatus.ullAvailPhys / bytesPerMb;

snapshot.MemoryTotalMb = totalMb;
snapshot.MemoryUsedMb = totalMb - availMb;
```

Обрати внимание на первое поле: `dwLength` заполняется размером самой структуры. Это
требование Windows API — так функция понимает, какая версия структуры ей передана (со
временем структуры расширяют новыми полями). Не заполнишь — вызов вернёт ошибку.

`ref` означает передачу по ссылке: функция пишет результат **в нашу** структуру, а не
возвращает новую.

Логика та же, что в Linux: доступная память вычитается из общей. `ullAvailPhys` — прямой
аналог `MemAvailable`.

### Загрузка процессора: как было и как стало

Изначально здесь был самый слабый код проекта. Работал он так: перебрать **все процессы
системы**, сложить их процессорное время, повторить через секунду и поделить разницу на
«прошедшее время × число ядер»:

```csharp
// так было — три проблемы в двадцати строках
private static TimeSpan GetTotalProcessorTime()
{
    var total = TimeSpan.Zero;
    foreach (var process in System.Diagnostics.Process.GetProcesses())
    {
        try
        {
            total += process.TotalProcessorTime;
        }
        catch
        {
        }
    }
    return total;
}
```

Что было не так:

1. **Неточность.** У системных процессов (`System`, `Registry`, службы под другими учётными
   записями) нет прав на чтение — обращение бросало исключение, пустой `catch` его глотал, и
   их время **просто не учитывалось**. Результат систематически занижен. Вдобавок между двумя
   замерами процессы рождаются и умирают: время умершего исчезало из суммы, и дельта могла
   получиться отрицательной — отсюда `Math.Clamp`, который прятал симптом.
2. **Дороговизна.** `Process.GetProcesses()` создаёт объект на каждый процесс системы (часто
   200–400 штук), и так дважды за замер — каждые шесть секунд ради одного числа.
3. **Молчаливое проглатывание.** Пустой `catch {}` без комментария не позволял отличить «мы
   знаем и не против» от «забыли обработать».

Стало — системный вызов **`GetSystemTimes`**, прямой аналог `/proc/stat`:

```csharp
[DllImport("kernel32.dll", SetLastError = true)]
[return: MarshalAs(UnmanagedType.Bool)]
private static extern bool GetSystemTimes(out FILETIME lpIdleTime, out FILETIME lpKernelTime, out FILETIME lpUserTime);

private static CpuTimes ReadWindowsCpuTimes()
{
    if (!GetSystemTimes(out var idleTime, out var kernelTime, out var userTime))
    {
        throw new InvalidOperationException(
            $"GetSystemTimes failed with Win32 error {Marshal.GetLastWin32Error()}.");
    }

    // kernelTime уже содержит время простоя, поэтому общее время — это kernel + user.
    var idle = ToTicks(idleTime);
    var total = ToTicks(kernelTime) + ToTicks(userTime);

    return new CpuTimes { Idle = idle, Total = total };
}
```

Функция возвращает три значения типа `FILETIME` — 64-битные счётчики, разбитые на две
32-битные половины. Склеиваем их обратно сдвигом:

```csharp
private static long ToTicks(FILETIME fileTime)
{
    return (long)(((ulong)fileTime.dwHighDateTime << 32) | fileTime.dwLowDateTime);
}
```

Выигрыш не только в точности и скорости. Счётчики Windows устроены так же, как в `/proc/stat`
(накопленное время простоя и накопленное общее время), поэтому **обе платформы теперь
считают загрузку одной и той же функцией** `ProcParser.CalculateCpuUsagePercent`. Формула
существует в единственном экземпляре, и тесты на неё покрывают сразу Linux и Windows.

Ещё одно изменение: вместо молчаливого выхода при ошибке теперь бросается исключение с кодом
ошибки Windows. Ноль в мониторинге выглядит как настоящее измерение — а это хуже, чем явная
ошибка, которую поймает и запишет `try/catch` фонового цикла.

Альтернатива, о которой стоит знать: **`PerformanceCounter("Processor", "% Processor Time",
"_Total")`** — то, что показывает «Диспетчер задач». Проще в использовании, но заметно
медленнее при первом обращении и тянет за собой дополнительную инфраструктуру счётчиков.

### Время работы системы

```csharp
snapshot.UptimeSeconds = Environment.TickCount64 / 1000.0;
```

`Environment.TickCount64` — миллисекунды с момента загрузки системы. Просто и правильно.
(Существовал ещё 32-битный `TickCount`, который переполнялся примерно через 49 дней — с
`TickCount64` этой проблемы нет.)

---

## Часть 4. Диск — общий код для обеих систем

```csharp
private static DriveInfo? FindSystemDrive()
{
    var readyDrives = DriveInfo.GetDrives()
        .Where(drive => drive.IsReady)
        .ToList();

    if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
    {
        return readyDrives.FirstOrDefault(drive => drive.Name == "/");
    }

    var systemRoot = Path.GetPathRoot(Environment.SystemDirectory);

    if (!string.IsNullOrEmpty(systemRoot))
    {
        var systemDrive = readyDrives.FirstOrDefault(drive =>
            string.Equals(drive.Name, systemRoot, StringComparison.OrdinalIgnoreCase));

        if (systemDrive is not null)
        {
            return systemDrive;
        }
    }

    return readyDrives.FirstOrDefault();
}
```

`DriveInfo` — кроссплатформенный класс .NET, поэтому P/Invoke не нужен.

- **`drive.IsReady`** — обязательная проверка: пустой привод или отключённый сетевой диск
  бросит исключение при обращении к размеру.
- **В Linux** ищется корневой раздел `/`.
- **В Windows** берётся раздел, на котором стоит система: `Environment.SystemDirectory` даёт
  путь вроде `C:\Windows\system32`, а `Path.GetPathRoot` отрезает от него корень `C:\`.

> **Как это исправили.** Раньше в Windows брался **первый попавшийся** готовый диск, и это
> была лотерея: обычно `C:`, но подключённый образ или карта памяти с меньшей буквой увели бы
> метрику на себя. Осталось честное упрощение: собирается только системный раздел. Полное
> решение — собирать данные по **всем** дискам, но оно требует изменения модели: сейчас в
> `MetricSnapshot` ровно одна пара полей под диск, а нужна коллекция.

Ещё одна тонкость: единицы. Код делит на 1024³, то есть считает **гибибайты** (ГиБ), а
подписывает их как GB. Производители дисков считают гигабайты по 1000³, поэтому «терабайтный»
диск покажется как 931 GB. Расхождение известное и не является ошибкой, но знать о нём стоит.

---

## Часть 5. Что стоит доработать

Исправлено (подробности — в [главе 09](09-fixing-the-defects.md)):

- ~~разбор чисел без указания культуры~~ — везде `CultureInfo.InvariantCulture`;
- ~~CPU в Windows через перебор процессов~~ — теперь `GetSystemTimes`;
- ~~выбор «первого попавшегося» диска~~ — теперь системный раздел;
- ~~нет проверки формата~~ — `lines[0]`, `parts[7]` и прочие обращения по индексу теперь
  защищены проверками длины, а при неожиданном содержимом бросается `FormatException` с самой
  проблемной строкой в тексте ошибки;
- ~~нет тестов~~ — разбор вынесен в `ProcParser`, на него написаны юнит-тесты, включая
  регрессионный на культуру.

Осталось на будущее:

- **Мало метрик.** Для реального мониторинга не хватает сети (принято/передано), нагрузки
  (`load average`), свопа, дискового ввода-вывода и — самое ценное — проверок сервисов:
  отвечает ли HTTP-эндпоинт, открыт ли порт, не истекает ли TLS-сертификат.
- **Только один диск.** Нужна коллекция разделов в модели данных.

---

## Что запомнить из главы

- Платформа определяется во время выполнения через `RuntimeInformation.IsOSPlatform`.
- В Linux данные берутся из виртуальных файлов `/proc`, в Windows — из функций системных
  библиотек через P/Invoke; диск в обоих случаях читается кроссплатформенным `DriveInfo`.
- Загрузку процессора нельзя прочитать мгновенно: ядро хранит накопленные счётчики, а
  загрузка — это разница между двумя замерами, поделённая на прошедшее время.
- `MemAvailable` (а не `MemFree`) — правильный показатель свободной памяти в Linux.
- `[StructLayout(LayoutKind.Sequential)]` и точное совпадение полей обязательны при P/Invoke,
  иначе структура будет прочитана неверно.
- Приведение `(double)` перед делением целых чисел — обязательно, иначе целочисленное деление
  даст ноль.
- Разбор чисел из машинных форматов должен идти через `CultureInfo.InvariantCulture`.

Дальше: глава 06 — Blazor Server: как C#-код отрисовывает интерфейс, что такое рендер-режимы
и почему страница обновляется сама без единой строчки JavaScript.
