using QueueManager.Domain;
using Xunit;

namespace QueueManager.Tests;

public class CustomerQueueTests
{
    private static CustomerQueue NewQueueWith(params string[] names)
    {
        var queue = new CustomerQueue();
        foreach (var name in names)
            queue.Enqueue(name, 30);
        return queue;
    }

    private static string[] Names(CustomerQueue queue) =>
        queue.Snapshot().Select(e => e.Customer.Name).ToArray();

    // ---------- Enqueue ----------

    [Fact]
    public void Enqueue_AddsToBack_AndReturnsPosition()
    {
        var queue = new CustomerQueue();

        var first = queue.Enqueue("Alice", 30);
        var second = queue.Enqueue("Bob", 40);

        Assert.Equal(1, first.Position);
        Assert.Equal(2, second.Position);
        Assert.Equal(2, queue.Count);
        Assert.Equal(new[] { "Alice", "Bob" }, Names(queue));
    }

    [Fact]
    public void Enqueue_TrimsName()
    {
        var queue = new CustomerQueue();
        var entry = queue.Enqueue("  Alice  ", 30);
        Assert.Equal("Alice", entry.Customer.Name);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Enqueue_RejectsEmptyName(string? name)
    {
        var queue = new CustomerQueue();
        Assert.Throws<ArgumentException>(() => queue.Enqueue(name!, 30));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(151)]
    public void Enqueue_RejectsOutOfRangeAge(int age)
    {
        var queue = new CustomerQueue();
        Assert.Throws<ArgumentOutOfRangeException>(() => queue.Enqueue("Alice", age));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(150)]
    public void Enqueue_AcceptsBoundaryAges(int age)
    {
        var queue = new CustomerQueue();
        var entry = queue.Enqueue("Alice", age);
        Assert.Equal(age, entry.Customer.Age);
    }

    // ---------- Dequeue ----------

    [Fact]
    public void Dequeue_IsFifo()
    {
        var queue = NewQueueWith("Alice", "Bob", "Cara");

        Assert.Equal("Alice", queue.Dequeue().Customer.Name);
        Assert.Equal("Bob", queue.Dequeue().Customer.Name);
        Assert.Equal("Cara", queue.Dequeue().Customer.Name);
        Assert.Equal(0, queue.Count);
    }

    [Fact]
    public void Dequeue_OnEmptyQueue_Throws()
    {
        var queue = new CustomerQueue();
        Assert.Throws<InvalidOperationException>(() => queue.Dequeue());
    }

    [Fact]
    public void TryDequeue_OnEmptyQueue_ReturnsFalse()
    {
        var queue = new CustomerQueue();
        Assert.False(queue.TryDequeue(out var served));
        Assert.Null(served);
    }

    [Fact]
    public void TryDequeue_ReturnsFrontCustomer()
    {
        var queue = NewQueueWith("Alice");
        Assert.True(queue.TryDequeue(out var served));
        Assert.Equal("Alice", served!.Customer.Name);
    }

    [Fact]
    public void Dequeue_ReportsWaitTime()
    {
        var clock = new FakeTimeProvider();
        var queue = new CustomerQueue(clock);
        queue.Enqueue("Alice", 30);

        clock.Advance(TimeSpan.FromMinutes(5));

        var served = queue.Dequeue();
        Assert.Equal(TimeSpan.FromMinutes(5), served.WaitTime);
    }

    // ---------- Peek ----------

    [Fact]
    public void Peek_ReturnsFront_WithoutRemoving()
    {
        var queue = NewQueueWith("Alice", "Bob");

        var entry = queue.Peek();

        Assert.NotNull(entry);
        Assert.Equal("Alice", entry!.Customer.Name);
        Assert.Equal(1, entry.Position);
        Assert.Equal(2, queue.Count);
    }

    [Fact]
    public void Peek_OnEmptyQueue_ReturnsNull()
    {
        var queue = new CustomerQueue();
        Assert.Null(queue.Peek());
    }

    // ---------- ReverseFirst ----------

    [Fact]
    public void ReverseFirst_ReversesOnlyTheFirstN()
    {
        var queue = NewQueueWith("A", "B", "C", "D", "E");

        var reversed = queue.ReverseFirst(3);

        Assert.Equal(3, reversed);
        Assert.Equal(new[] { "C", "B", "A", "D", "E" }, Names(queue));
    }

    [Fact]
    public void ReverseFirst_Zero_IsNoOp()
    {
        var queue = NewQueueWith("A", "B", "C");

        var reversed = queue.ReverseFirst(0);

        Assert.Equal(0, reversed);
        Assert.Equal(new[] { "A", "B", "C" }, Names(queue));
    }

    [Fact]
    public void ReverseFirst_One_IsNoOp()
    {
        var queue = NewQueueWith("A", "B", "C");

        var reversed = queue.ReverseFirst(1);

        Assert.Equal(1, reversed);
        Assert.Equal(new[] { "A", "B", "C" }, Names(queue));
    }

    [Fact]
    public void ReverseFirst_ExactlyCount_ReversesWholeQueue()
    {
        var queue = NewQueueWith("A", "B", "C");

        queue.ReverseFirst(3);

        Assert.Equal(new[] { "C", "B", "A" }, Names(queue));
    }

    [Fact]
    public void ReverseFirst_GreaterThanCount_ClampsAndReversesWholeQueue()
    {
        var queue = NewQueueWith("A", "B", "C");

        var reversed = queue.ReverseFirst(99);

        Assert.Equal(3, reversed);
        Assert.Equal(new[] { "C", "B", "A" }, Names(queue));
    }

    [Fact]
    public void ReverseFirst_OnEmptyQueue_IsNoOp()
    {
        var queue = new CustomerQueue();

        var reversed = queue.ReverseFirst(5);

        Assert.Equal(0, reversed);
        Assert.Equal(0, queue.Count);
    }

    [Fact]
    public void ReverseFirst_Negative_Throws()
    {
        var queue = NewQueueWith("A");
        Assert.Throws<ArgumentOutOfRangeException>(() => queue.ReverseFirst(-1));
    }

    [Fact]
    public void ReverseFirst_PreservesEnqueueTimestamps()
    {
        var clock = new FakeTimeProvider();
        var queue = new CustomerQueue(clock);
        queue.Enqueue("A", 30);
        clock.Advance(TimeSpan.FromMinutes(1));
        queue.Enqueue("B", 30);

        queue.ReverseFirst(2);

        var snapshot = queue.Snapshot();
        Assert.Equal("B", snapshot[0].Customer.Name);
        // B has waited 0 minutes even though B is now at the front.
        Assert.Equal(TimeSpan.Zero, snapshot[0].WaitTime);
        Assert.Equal(TimeSpan.FromMinutes(1), snapshot[1].WaitTime);
    }

    // ---------- Position / wait-time tracking ----------

    [Fact]
    public void Find_ReturnsPositionAndWaitTime()
    {
        var clock = new FakeTimeProvider();
        var queue = new CustomerQueue(clock);
        queue.Enqueue("Alice", 30);
        var bob = queue.Enqueue("Bob", 40);

        clock.Advance(TimeSpan.FromMinutes(10));

        var entry = queue.Find(bob.Customer.Id);
        Assert.NotNull(entry);
        Assert.Equal(2, entry!.Position);
        Assert.Equal(TimeSpan.FromMinutes(10), entry.WaitTime);
    }

    [Fact]
    public void Find_PositionAdvancesAfterDequeue()
    {
        var queue = NewQueueWith("Alice");
        var bob = queue.Enqueue("Bob", 40);

        queue.Dequeue();

        Assert.Equal(1, queue.Find(bob.Customer.Id)!.Position);
    }

    [Fact]
    public void Find_UnknownId_ReturnsNull()
    {
        var queue = NewQueueWith("Alice");
        Assert.Null(queue.Find(Guid.NewGuid()));
    }

    [Fact]
    public void Snapshot_AssignsSequentialPositions()
    {
        var queue = NewQueueWith("A", "B", "C");
        Assert.Equal(new[] { 1, 2, 3 }, queue.Snapshot().Select(e => e.Position));
    }

    // ---------- Stats ----------

    [Fact]
    public void Stats_OnEmptyQueue_AreZero()
    {
        var stats = new CustomerQueue().GetStats();

        Assert.Equal(0, stats.CurrentLength);
        Assert.Equal(0, stats.TotalServed);
        Assert.Equal(TimeSpan.Zero, stats.AverageWaitTime);
        Assert.Equal(TimeSpan.Zero, stats.LongestCurrentWait);
    }

    [Fact]
    public void Stats_TrackServedCountAndAverageWait()
    {
        var clock = new FakeTimeProvider();
        var queue = new CustomerQueue(clock);
        queue.Enqueue("A", 30);
        queue.Enqueue("B", 30);

        clock.Advance(TimeSpan.FromMinutes(2));
        queue.Dequeue(); // A waited 2 min
        clock.Advance(TimeSpan.FromMinutes(2));
        queue.Dequeue(); // B waited 4 min

        var stats = queue.GetStats();
        Assert.Equal(2, stats.TotalServed);
        Assert.Equal(TimeSpan.FromMinutes(3), stats.AverageWaitTime);
        Assert.Equal(0, stats.CurrentLength);
    }

    [Fact]
    public void Stats_ReportLongestCurrentWait()
    {
        var clock = new FakeTimeProvider();
        var queue = new CustomerQueue(clock);
        queue.Enqueue("A", 30);
        clock.Advance(TimeSpan.FromMinutes(7));
        queue.Enqueue("B", 30);

        var stats = queue.GetStats();
        Assert.Equal(2, stats.CurrentLength);
        Assert.Equal(TimeSpan.FromMinutes(7), stats.LongestCurrentWait);
    }
}
