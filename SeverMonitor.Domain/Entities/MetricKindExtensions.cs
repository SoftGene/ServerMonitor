namespace ServerMonitor.Domain.Entities;

public static class MetricKindExtensions
{
    /// <summary>
    /// Человекочитаемое имя метрики. Оно же используется как значение в базе, чтобы
    /// записи, сделанные до перехода на перечисления, продолжали читаться.
    /// </summary>
    public static string ToDisplayName(this MetricKind kind) =>
        kind == MetricKind.Cpu ? "CPU" : kind.ToString();
}
