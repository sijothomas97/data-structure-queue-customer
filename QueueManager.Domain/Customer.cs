namespace QueueManager.Domain;

/// <summary>An immutable customer waiting in the queue.</summary>
public sealed record Customer
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public required string Name { get; init; }

    public required int Age { get; init; }

    /// <summary>UTC instant at which the customer joined the queue.</summary>
    public DateTimeOffset EnqueuedAt { get; init; }
}
