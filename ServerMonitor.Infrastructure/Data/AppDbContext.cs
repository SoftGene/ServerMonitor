using Microsoft.EntityFrameworkCore;
using ServerMonitor.Domain.Entities;

namespace ServerMonitor.Infrastructure.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public DbSet<MetricSnapshot> MetricSnapshots => Set<MetricSnapshot>();
    public DbSet<Alert> Alerts => Set<Alert>();
    public DbSet<AppSettings> AppSettings => Set<AppSettings>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<AppSettings>().HasData(new AppSettings
        {
            Id = 1,
            CpuThreshold = 90,
            MemoryThreshold = 90,
            DiskThreshold = 90,
            AlertsEnabled = true
        });
    }
}