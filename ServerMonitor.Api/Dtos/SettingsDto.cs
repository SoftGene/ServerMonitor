namespace ServerMonitor.Api.Dtos;

public class SettingsDto
{
    public double CpuThreshold { get; set; }
    public double MemoryThreshold { get; set; }
    public double DiskThreshold { get; set; }
    public bool AlertsEnabled { get; set; }
}