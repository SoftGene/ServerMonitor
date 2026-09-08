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
            // The public id is looked up on every request from the UI and the key hash on
            // every metric that arrives; both need an index.
            entity.HasIndex(s => s.PublicId).IsUnique();
            entity.HasIndex(s => s.ApiKeyHash);
        });

        modelBuilder.Entity<MetricSnapshot>(entity =>
        {
            // Percentages are derived from the absolute values and have no columns.
            entity.Ignore(m => m.MemoryUsagePercent);
            entity.Ignore(m => m.DiskUsagePercent);

            // Every query in the project sorts and filters on this column. Without an index
            // PostgreSQL reads and sorts the whole table to return a single latest row.
            entity.HasIndex(m => m.TimestampUtc);

            // Dashboard queries always target one machine and order by time, so a composite
            // index covers them completely.
            entity.HasIndex(m => new { m.ServerId, m.TimestampUtc });

            entity.HasOne<Server>()
                .WithMany()
                .HasForeignKey(m => m.ServerId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Alert>(entity =>
        {
            // Enums are stored as text, exactly as before: old rows ("CPU", "Triggered")
            // keep reading back and no data migration is needed.
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
            // Username uniqueness is enforced by the database, not only by a check in code:
            // two simultaneous registrations would otherwise both succeed.
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
