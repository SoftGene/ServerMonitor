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

        modelBuilder.Entity<MetricSnapshot>(entity =>
        {
            // Проценты вычисляются из абсолютных значений и колонок в базе не имеют.
            entity.Ignore(m => m.MemoryUsagePercent);
            entity.Ignore(m => m.DiskUsagePercent);

            // По этой колонке сортирует и фильтрует каждый запрос проекта. Без индекса
            // PostgreSQL читает и сортирует всю таблицу, чтобы отдать одну последнюю строку.
            entity.HasIndex(m => m.TimestampUtc);
        });

        modelBuilder.Entity<Alert>(entity =>
        {
            // Перечисления хранятся в базе строками, как и раньше: старые записи
            // ("CPU", "Triggered") продолжают читаться, миграция данных не нужна.
            entity.Property(a => a.MetricType)
                .HasConversion(
                    kind => kind.ToDisplayName(),
                    value => Enum.Parse<MetricKind>(value, ignoreCase: true));

            entity.Property(a => a.AlertType)
                .HasConversion(
                    kind => kind.ToString(),
                    value => Enum.Parse<AlertKind>(value, ignoreCase: true));

            entity.HasIndex(a => a.TimestampUtc);
        });

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
