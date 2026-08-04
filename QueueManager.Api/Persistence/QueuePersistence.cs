using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using QueueManager.Domain;

namespace QueueManager.Api.Persistence;

/// <summary>
/// Persists the in-memory <see cref="CustomerQueue"/> to SQLite so the queue (and its
/// served-customer metrics) survive restarts. The queue is small, so every mutation
/// rewrites the waiting-customers table inside a single transaction — boring and correct.
/// </summary>
public sealed class QueuePersistence
{
    private readonly IDbContextFactory<QueueDbContext> _dbFactory;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public QueuePersistence(IDbContextFactory<QueueDbContext> dbFactory) => _dbFactory = dbFactory;

    /// <summary>Creates the schema if needed and restores persisted state into <paramref name="queue"/>.</summary>
    public async Task InitializeAsync(CustomerQueue queue, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        await db.Database.EnsureCreatedAsync(cancellationToken);

        var waiting = await db.Queue.AsNoTracking()
            .OrderBy(r => r.Position)
            .ToListAsync(cancellationToken);

        var totalServed = await db.Served.CountAsync(cancellationToken);
        var cumulativeWaitSeconds = totalServed == 0
            ? 0d
            : await db.Served.SumAsync(r => r.WaitSeconds, cancellationToken);

        queue.Restore(
            waiting.Select(r => new Customer
            {
                Id = r.Id,
                Name = r.Name,
                Age = r.Age,
                EnqueuedAt = r.EnqueuedAt,
            }),
            totalServed,
            TimeSpan.FromSeconds(cumulativeWaitSeconds));
    }

    /// <summary>Persists the current queue snapshot (after enqueue/reverse).</summary>
    public Task SaveQueueAsync(IReadOnlyList<QueueEntry> snapshot, CancellationToken cancellationToken = default) =>
        WriteAsync(db => ReplaceQueueRows(db, snapshot), cancellationToken);

    /// <summary>Persists a dequeue: records the served customer and rewrites the remaining queue.</summary>
    public Task SaveDequeueAsync(
        ServedCustomer served,
        DateTimeOffset servedAt,
        IReadOnlyList<QueueEntry> snapshot,
        CancellationToken cancellationToken = default) =>
        WriteAsync(
            db =>
            {
                db.Served.Add(new ServedCustomerRow
                {
                    CustomerId = served.Customer.Id,
                    Name = served.Customer.Name,
                    Age = served.Customer.Age,
                    EnqueuedAt = served.Customer.EnqueuedAt,
                    ServedAt = servedAt,
                    ServedAtUtcTicks = servedAt.UtcTicks,
                    WaitSeconds = served.WaitTime.TotalSeconds,
                });
                ReplaceQueueRows(db, snapshot);
            },
            cancellationToken);

    private static void ReplaceQueueRows(QueueDbContext db, IReadOnlyList<QueueEntry> snapshot)
    {
        db.Queue.RemoveRange(db.Queue);
        for (var i = 0; i < snapshot.Count; i++)
        {
            var customer = snapshot[i].Customer;
            db.Queue.Add(new QueuedCustomerRow
            {
                Id = customer.Id,
                Name = customer.Name,
                Age = customer.Age,
                EnqueuedAt = customer.EnqueuedAt,
                Position = i,
            });
        }
    }

    private async Task WriteAsync(Action<QueueDbContext> mutate, CancellationToken cancellationToken)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
            mutate(db);
            await db.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>Ensures the directory containing the SQLite file exists (no-op for in-memory).</summary>
    public static void EnsureDatabaseDirectory(string connectionString)
    {
        var dataSource = new SqliteConnectionStringBuilder(connectionString).DataSource;
        if (string.IsNullOrWhiteSpace(dataSource) || dataSource == ":memory:")
            return;

        var directory = Path.GetDirectoryName(Path.GetFullPath(dataSource));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
    }
}
