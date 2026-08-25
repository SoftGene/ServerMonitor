namespace ServerMonitor.Domain.Entities;

public class AppSettings
{
    public int Id { get; set; }
    public double CpuThreshold { get; set; }
    public double MemoryThreshold { get; set; }
    public double DiskThreshold { get; set; }
    public bool AlertsEnabled { get; set; }
}