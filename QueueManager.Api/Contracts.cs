using QueueManager.Domain;

namespace QueueManager.Api;

/// <summary>Request body for enqueuing a customer.</summary>
public sealed record EnqueueRequest(string Name, int Age);

/// <summary>A customer's status in the queue, as returned by the API.</summary>
public sealed record QueueEntryDto(Guid Id, string Name, int Age, DateTimeOffset EnqueuedAt, int Position, double WaitSeconds)
{
    public static QueueEntryDto From(QueueEntry entry) => new(
        entry.Customer.Id,
        entry.Customer.Name,
        entry.Customer.Age,
        entry.Customer.EnqueuedAt,
        entry.Position,
        Math.Round(entry.WaitTime.TotalSeconds, 3));
}

/// <summary>A served (dequeued) customer.</summary>
public sealed record ServedCustomerDto(Guid Id, string Name, int Age, DateTimeOffset EnqueuedAt, double WaitSeconds)
{
    public static ServedCustomerDto From(ServedCustomer served) => new(
        served.Customer.Id,
        served.Customer.Name,
        served.Customer.Age,
        served.Customer.EnqueuedAt,
        Math.Round(served.WaitTime.TotalSeconds, 3));
}

/// <summary>Result of a reverse-first-N operation.</summary>
public sealed record ReverseResultDto(int Requested, int Reversed, IReadOnlyList<QueueEntryDto> Queue);

/// <summary>Durable throughput and wait metrics (survive restarts via SQLite).</summary>
public sealed record MetricsDto(
    int CurrentLength,
    int TotalServed,
    double AverageWaitSeconds,
    double LongestCurrentWaitSeconds,
    int ServedLastHour,
    double ThroughputPerMinuteLastHour);

/// <summary>Aggregate queue metrics.</summary>
public sealed record QueueStatsDto(int CurrentLength, int TotalServed, double AverageWaitSeconds, double LongestCurrentWaitSeconds)
{
    public static QueueStatsDto From(QueueStats stats) => new(
        stats.CurrentLength,
        stats.TotalServed,
        Math.Round(stats.AverageWaitTime.TotalSeconds, 3),
        Math.Round(stats.LongestCurrentWait.TotalSeconds, 3));
}
