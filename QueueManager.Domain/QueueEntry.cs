namespace QueueManager.Domain;

/// <summary>A customer's current status inside the queue.</summary>
/// <param name="Customer">The customer.</param>
/// <param name="Position">1-based position in the queue (1 = next to be served).</param>
/// <param name="WaitTime">How long the customer has been waiting so far.</param>
public sealed record QueueEntry(Customer Customer, int Position, TimeSpan WaitTime);

/// <summary>The result of serving (dequeuing) a customer.</summary>
/// <param name="Customer">The customer that was served.</param>
/// <param name="WaitTime">Total time the customer spent in the queue.</param>
public sealed record ServedCustomer(Customer Customer, TimeSpan WaitTime);

/// <summary>Aggregate throughput/wait metrics for the queue.</summary>
/// <param name="CurrentLength">Number of customers currently waiting.</param>
/// <param name="TotalServed">Customers dequeued since startup.</param>
/// <param name="AverageWaitTime">Mean wait time of served customers (zero when none served).</param>
/// <param name="LongestCurrentWait">Longest wait among customers still in the queue (zero when empty).</param>
public sealed record QueueStats(
    int CurrentLength,
    int TotalServed,
    TimeSpan AverageWaitTime,
    TimeSpan LongestCurrentWait);
