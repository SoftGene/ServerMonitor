# Глава 05 — Как снимаются метрики

Единственное место проекта, где код разговаривает не с базой и не с браузером, а напрямую с
операционной системой. Здесь два совершенно разных подхода — файлы в Linux и системные вызовы
в Windows — и одна нетривиальная идея: загрузку процессора невозможно измерить мгновенно.

Весь разбор — по файлу
[`MetricsCollector.cs`](../../ServerMonitor.Infrastructure/Monitoring/MetricsCollector.cs).

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

> **Слабое место — и оно серьёзное.** `double.Parse(parts[0])` без указания культуры
> использует **культуру текущего потока**. В `/proc/uptime` дробная часть отделена **точкой**
> — это машинный формат, он не зависит от языка системы. Но если процесс запущен с русской
> культурой, разделителем считается запятая, и строка `"348915.42"` разбором не примется:
> `double.Parse` бросит `FormatException`.
>
> Сейчас баг не проявляется: на Windows этот код не выполняется, а на Linux у служб обычно
> инвариантная культура или `en_US`. Но это мина: достаточно запустить сервис с
> `LANG=ru_RU.UTF-8`, и сбор метрик начнёт падать каждые несколько секунд — правда, будет
> аккуратно логироваться благодаря `try/catch` из главы 04.
>
> Лечение — то же правило, что и в истории с шириной прогресс-баров (глава 01):
> ```csharp
> return double.Parse(parts[1], CultureInfo.InvariantCulture);
> ```
> **Данные от машины разбирай инвариантной культурой.** Это касается и `ParseMemInfoLine`,
> хотя там значения целые и в большинстве культур пройдут.

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

### Загрузка процессора — и почему тут всё плохо

```csharp
private static async Task CollectWindowsCpuAsync(MetricSnapshot snapshot, CancellationToken cancellationToken)
{
    var startCpu = GetTotalProcessorTime();
    var startTime = DateTime.UtcNow;

    await Task.Delay(1000, cancellationToken);

    var endCpu = GetTotalProcessorTime();
    var endTime = DateTime.UtcNow;

    var cpuUsedMs = (endCpu - startCpu).TotalMilliseconds;
    var elapsedMs = (endTime - startTime).TotalMilliseconds;
    var cpuCount = Environment.ProcessorCount;

    var usage = cpuUsedMs / (elapsedMs * cpuCount) * 100.0;
    snapshot.CpuUsagePercent = Math.Round(Math.Clamp(usage, 0, 100), 2);
}
```

Идея та же самая — дельта за секунду. Но источник данных другой:

```csharp
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

Метод перебирает **все процессы системы** и складывает их процессорное время. Формула затем
делит полученное на «прошедшее время × число ядер» — то есть на максимально возможное
процессорное время за интервал.

Проблем здесь три, и их стоит понимать:

1. **Неточность.** У системных процессов (`System`, `Registry`, службы под другими учётными
   записями) нет прав на чтение — обращение бросает исключение, пустой `catch` его глотает, и
   их время **просто не учитывается**. Результат систематически занижен. Плюс между двумя
   замерами процессы рождаются и умирают: время умершего исчезает из суммы, и дельта может
   получиться отрицательной — отсюда и `Math.Clamp(usage, 0, 100)`, который прячет симптом.
2. **Дороговизна.** `Process.GetProcesses()` создаёт объект на каждый процесс системы (часто
   200–400 штук), и так дважды за замер. Это заметная работа каждые шесть секунд ради одного
   числа.
3. **Молчаливое проглатывание.** Пустой `catch {}` без комментария — самая спорная строка
   проекта. Исключение здесь ожидаемо, но из кода это не следует; читатель не может отличить
   «мы знаем и не против» от «забыли обработать».

Правильные варианты для Windows:

- **`GetSystemTimes`** из `kernel32.dll` — прямой аналог `/proc/stat`: отдаёт время простоя,
  ядра и пользователя по системе целиком. Один вызов вместо перебора процессов, и данные
  честные.
- **`PerformanceCounter("Processor", "% Processor Time", "_Total")`** — то, что показывает
  «Диспетчер задач»; проще в использовании, но заметно медленнее при первом обращении.

Учитывая, что P/Invoke в файле уже есть, первый вариант напрашивается сам собой — это хорошая
задача на доработку.

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
private static void CollectDiskMetrics(MetricSnapshot snapshot)
{
    DriveInfo? targetDrive = null;

    foreach (var drive in DriveInfo.GetDrives())
    {
        if (!drive.IsReady) continue;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            if (drive.Name == "/") { targetDrive = drive; break; }
        }
        else
        {
            targetDrive = drive;
            break;
        }
    }

    if (targetDrive is null) return;

    double totalBytes = targetDrive.TotalSize;
    double freeBytes = targetDrive.AvailableFreeSpace;
    double usedBytes = totalBytes - freeBytes;

    const double bytesPerGb = 1024.0 * 1024.0 * 1024.0;
    snapshot.DiskTotalGb = totalBytes / bytesPerGb;
    snapshot.DiskUsedGb = usedBytes / bytesPerGb;
}
```

`DriveInfo` — кроссплатформенный класс .NET, поэтому P/Invoke не нужен.

- **`drive.IsReady`** — обязательная проверка: пустой привод или отключённый сетевой диск
  бросит исключение при обращении к размеру.
- **В Linux** ищется корневой раздел `/` — осмысленный выбор.
- **В Windows** берётся **первый попавшийся** готовый диск.

> **Слабое место.** «Первый попавшийся» — это лотерея. Обычно им окажется `C:`, но если в
> системе есть, скажем, подключённый образ или карта памяти с меньшей буквой, метрика будет
> про неё. Правильнее либо явно брать системный диск
> (`Path.GetPathRoot(Environment.SystemDirectory)`), либо — что честнее для мониторинга —
> собирать данные по **всем** дискам. Второй вариант потребует изменения модели: сейчас в
> `MetricSnapshot` ровно одна пара полей под диск, а нужна коллекция.

Ещё одна тонкость: единицы. Код делит на 1024³, то есть считает **гибибайты** (ГиБ), а
подписывает их как GB. Производители дисков считают гигабайты по 1000³, поэтому «терабайтный»
диск покажется как 931 GB. Расхождение известное и не является ошибкой, но знать о нём стоит.

---

## Часть 5. Что стоит доработать

- **Разбор чисел без указания культуры** — латентная ошибка, разобранная выше. Самое важное
  из этой главы к исправлению.
- **CPU в Windows** — перейти на `GetSystemTimes` вместо перебора процессов.
- **Выбор диска** — брать системный или собирать все.
- **Нет проверки формата.** `lines[0]`, `parts[7]`, `parts[1]` — обращения по индексу без
  проверки длины. Если файл окажется другим (экзотическое ядро, контейнер с урезанным
  `/proc`), получим `IndexOutOfRangeException`. Цикл сбора это переживёт благодаря `try/catch`,
  но в журнале будет невнятная ошибка вместо понятного сообщения.
- **Мало метрик.** Для реального мониторинга не хватает сети (принято/передано), нагрузки
  (`load average`), свопа, дискового ввода-вывода и — самое ценное — проверок сервисов:
  отвечает ли HTTP-эндпоинт, открыт ли порт, не истекает ли TLS-сертификат.
- **Нет тестов.** Парсеры текстовых форматов ломаются первыми, а проверить их просто:
  вынести разбор в отдельный метод, принимающий строку, и скормить ему образцы содержимого
  `/proc`. Отличная первая задача на юнит-тесты в этом проекте — как раз тот случай, когда
  тесты пишутся легко и приносят реальную пользу.

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
