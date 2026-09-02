namespace ServerMonitor.Domain.Entities;

/// <summary>Метрика, к которой относится событие.</summary>
public enum MetricKind
{
    Cpu,
    Memory,
    Disk
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
