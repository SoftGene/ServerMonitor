namespace ServerMonitor.Domain.Entities;

/// <summary>Метрика, к которой относится событие.</summary>
public enum MetricKind
{
    Cpu,
    Memory,
    Disk,

    /// <summary>
    /// Доступность машины. Строго говоря, это не метрика: значение не приходит от агента,
    /// а выводится из того, что данных нет. Событие живёт в общем журнале, потому что по
    /// природе оно то же самое — у него есть машина, время и переходы «началось/закончилось».
    /// Добавлено последним: значения хранятся в базе строками, порядок ни на что не влияет.
    /// </summary>
    Availability
}

/// <summary>Тип события: порог превышен или значение вернулось в норму.</summary>
public enum AlertKind
{
    Triggered,
    Recovered
}

public class Alert
{
    public int Id { get; set; }

    /// <summary>Машина, к которой относится событие.</summary>
    public int ServerId { get; set; }
    public DateTime TimestampUtc { get; set; }

    /// <summary>
    /// Раньше здесь была строка, и опечатка вроде "Trigered" не остановила бы компилятор.
    /// В базе значения по-прежнему хранятся текстом — за это отвечает конвертер значений
    /// в AppDbContext, поэтому старые записи читаются без миграции данных.
    /// </summary>
    public MetricKind MetricType { get; set; }

    public double Value { get; set; }
    public double Threshold { get; set; }
    public AlertKind AlertType { get; set; }
}
