using Microsoft.EntityFrameworkCore;

namespace QueueManager.Api.Persistence;

/// <summary>A customer currently waiting, as stored in SQLite.</summary>
public sealed class QueuedCustomerRow
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public int Age { get; set; }

    public DateTimeOffset EnqueuedAt { get; set; }

    /// <summary>0-based order in the queue (0 = front).</summary>
    public int Position { get; set; }
}

/// <summary>A served (dequeued) customer, kept for durable throughput/wait metrics.</summary>
public sealed class ServedCustomerRow
{
    public long RowId { get; set; }

    public Guid CustomerId { get; set; }

    public string Name { get; set; } = string.Empty;

    public int Age { get; set; }

    public DateTimeOffset EnqueuedAt { get; set; }

    public DateTimeOffset ServedAt { get; set; }

    /// <summary>UTC ticks of <see cref="ServedAt"/>; used for range queries (SQLite cannot compare DateTimeOffset).</summary>
    public long ServedAtUtcTicks { get; set; }

    public double WaitSeconds { get; set; }
}

public sealed class QueueDbContext : DbContext
{
    public QueueDbContext(DbContextOptions<QueueDbContext> options)
        : base(options)
    {
    }

    public DbSet<QueuedCustomerRow> Queue => Set<QueuedCustomerRow>();

    public DbSet<ServedCustomerRow> Served => Set<ServedCustomerRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<QueuedCustomerRow>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired();
            entity.HasIndex(e => e.Position);
        });

        modelBuilder.Entity<ServedCustomerRow>(entity =>
        {
            entity.HasKey(e => e.RowId);
            entity.Property(e => e.Name).IsRequired();
            entity.HasIndex(e => e.ServedAtUtcTicks);
        });
    }
}
