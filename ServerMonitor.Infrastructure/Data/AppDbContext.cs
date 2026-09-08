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
    public DbSet<User> Users => Set<User>();
    public DbSet<Server> Servers => Set<Server>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Server>(entity =>
        {
            // Публичный идентификатор ищется по каждому запросу от UI, ключ — по каждому
            // приёму метрик; оба должны быть проиндексированы.
            entity.HasIndex(s => s.PublicId).IsUnique();
            entity.HasIndex(s => s.ApiKeyHash);
        });

        modelBuilder.Entity<MetricSnapshot>(entity =>
        {
            // Проценты вычисляются из абсолютных значений и колонок в базе не имеют.
            entity.Ignore(m => m.MemoryUsagePercent);
            entity.Ignore(m => m.DiskUsagePercent);

            // По этой колонке сортирует и фильтрует каждый запрос проекта. Без индекса
            // PostgreSQL читает и сортирует всю таблицу, чтобы отдать одну последнюю строку.
            entity.HasIndex(m => m.TimestampUtc);

            // Запросы дашборда всегда идут по одной машине и с сортировкой по времени —
            // составной индекс покрывает их целиком.
            entity.HasIndex(m => new { m.ServerId, m.TimestampUtc });

            entity.HasOne<Server>()
                .WithMany()
                .HasForeignKey(m => m.ServerId)
                .OnDelete(DeleteBehavior.Cascade);
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

            entity.HasOne<Server>()
                .WithMany()
                .HasForeignKey(a => a.ServerId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<User>(entity =>
        {
            // Уникальность имени — на уровне базы, а не только проверкой в коде: две
            // одновременные регистрации иначе прошли бы обе.
            entity.HasIndex(u => u.Username).IsUnique();
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
