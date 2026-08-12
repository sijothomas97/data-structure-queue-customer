namespace QueueManager.Domain;

/// <summary>
/// Thread-safe FIFO customer queue with position and wait-time tracking,
/// plus the classic "reverse the first N" operation from the original app.
/// </summary>
public sealed class CustomerQueue
{
    private readonly object _gate = new();
    private readonly List<Customer> _items = new();
    private readonly TimeProvider _clock;

    private int _totalServed;
    private TimeSpan _cumulativeServedWait = TimeSpan.Zero;

    public CustomerQueue(TimeProvider? clock = null) => _clock = clock ?? TimeProvider.System;

    public int Count
    {
        get { lock (_gate) return _items.Count; }
    }

    /// <summary>Adds a customer to the back of the queue.</summary>
    /// <returns>The entry, including the customer's 1-based position.</returns>
    /// <exception cref="ArgumentException">Name is null/whitespace.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Age outside 0–150.</exception>
    public QueueEntry Enqueue(string name, int age)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Customer name must not be empty.", nameof(name));
        if (age is < 0 or > 150)
            throw new ArgumentOutOfRangeException(nameof(age), age, "Age must be between 0 and 150.");

        var customer = new Customer
        {
            Name = name.Trim(),
            Age = age,
            EnqueuedAt = _clock.GetUtcNow(),
        };

        lock (_gate)
        {
            _items.Add(customer);
            return new QueueEntry(customer, _items.Count, TimeSpan.Zero);
        }
    }

    /// <summary>Removes and returns the customer at the front of the queue.</summary>
    /// <exception cref="InvalidOperationException">The queue is empty.</exception>
    public ServedCustomer Dequeue()
    {
        lock (_gate)
        {
            if (_items.Count == 0)
                throw new InvalidOperationException("The queue is empty.");

            var customer = _items[0];
            _items.RemoveAt(0);

            var wait = ClampNonNegative(_clock.GetUtcNow() - customer.EnqueuedAt);
            _totalServed++;
            _cumulativeServedWait += wait;
            return new ServedCustomer(customer, wait);
        }
    }

    public bool TryDequeue(out ServedCustomer? served)
    {
        lock (_gate)
        {
            if (_items.Count == 0)
            {
                served = null;
                return false;
            }

            served = Dequeue();
            return true;
        }
    }

    /// <summary>Returns the customer at the front without removing them, or null when empty.</summary>
    public QueueEntry? Peek()
    {
        lock (_gate)
        {
            if (_items.Count == 0) return null;
            var customer = _items[0];
            return new QueueEntry(customer, 1, ClampNonNegative(_clock.GetUtcNow() - customer.EnqueuedAt));
        }
    }

    /// <summary>
    /// Reverses the order of the first <paramref name="count"/> customers, leaving the rest untouched.
    /// <c>count</c> of 0 or 1 is a no-op; a <c>count</c> larger than the queue length reverses the
    /// whole queue (it is clamped, matching the intent of "reverse the first N that exist").
    /// </summary>
    /// <returns>The number of customers actually reversed.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> is negative.</exception>
    public int ReverseFirst(int count)
    {
        if (count < 0)
            throw new ArgumentOutOfRangeException(nameof(count), count, "Count must not be negative.");

        lock (_gate)
        {
            var n = Math.Min(count, _items.Count);
            if (n > 1)
                _items.Reverse(0, n);
            return n;
        }
    }

    /// <summary>
    /// Replaces the queue's entire state, restoring it from a persisted snapshot
    /// (e.g. on application startup). Order of <paramref name="customers"/> is front-first.
    /// </summary>
    /// <param name="customers">Customers to place in the queue, front first.</param>
    /// <param name="totalServed">Number of customers served before the snapshot was taken.</param>
    /// <param name="cumulativeServedWait">Total wait time accumulated by served customers.</param>
    public void Restore(IEnumerable<Customer> customers, int totalServed, TimeSpan cumulativeServedWait)
    {
        ArgumentNullException.ThrowIfNull(customers);
        if (totalServed < 0)
            throw new ArgumentOutOfRangeException(nameof(totalServed), totalServed, "Total served must not be negative.");
        if (cumulativeServedWait < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(cumulativeServedWait), cumulativeServedWait, "Cumulative wait must not be negative.");

        lock (_gate)
        {
            _items.Clear();
            _items.AddRange(customers);
            _totalServed = totalServed;
            _cumulativeServedWait = cumulativeServedWait;
        }
    }

    /// <summary>Finds a customer's current entry (position + wait time) by id, or null.</summary>
    public QueueEntry? Find(Guid customerId)
    {
        lock (_gate)
        {
            for (var i = 0; i < _items.Count; i++)
            {
                if (_items[i].Id == customerId)
                    return new QueueEntry(_items[i], i + 1, ClampNonNegative(_clock.GetUtcNow() - _items[i].EnqueuedAt));
            }

            return null;
        }
    }

    /// <summary>An ordered snapshot of everyone currently in the queue.</summary>
    public IReadOnlyList<QueueEntry> Snapshot()
    {
        lock (_gate)
        {
            var now = _clock.GetUtcNow();
            var result = new List<QueueEntry>(_items.Count);
            for (var i = 0; i < _items.Count; i++)
                result.Add(new QueueEntry(_items[i], i + 1, ClampNonNegative(now - _items[i].EnqueuedAt)));
            return result;
        }
    }

    public QueueStats GetStats()
    {
        lock (_gate)
        {
            var now = _clock.GetUtcNow();
            var longest = TimeSpan.Zero;
            foreach (var item in _items)
            {
                var wait = ClampNonNegative(now - item.EnqueuedAt);
                if (wait > longest) longest = wait;
            }

            var average = _totalServed == 0 ? TimeSpan.Zero : _cumulativeServedWait / _totalServed;
            return new QueueStats(_items.Count, _totalServed, average, longest);
        }
    }

    private static TimeSpan ClampNonNegative(TimeSpan value) => value < TimeSpan.Zero ? TimeSpan.Zero : value;
}
