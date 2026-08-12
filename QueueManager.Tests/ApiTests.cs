using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using QueueManager.Api;
using Xunit;

namespace QueueManager.Tests;

public class ApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ApiTests(WebApplicationFactory<Program> factory) => _factory = factory;

    /// <summary>Each test gets an isolated app (isolated singleton queue and its own SQLite file).</summary>
    private HttpClient NewClient() =>
        _factory.WithWebHostBuilder(builder =>
            builder.UseSetting(TestDb.ConnectionStringKey, TestDb.NewConnectionString())).CreateClient();

    private static async Task<QueueEntryDto> EnqueueAsync(HttpClient client, string name, int age)
    {
        var response = await client.PostAsJsonAsync("/api/queue", new EnqueueRequest(name, age));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<QueueEntryDto>())!;
    }

    // ---------- Enqueue ----------

    [Fact]
    public async Task Enqueue_ReturnsCreatedWithPosition()
    {
        var client = NewClient();

        var alice = await EnqueueAsync(client, "Alice", 30);
        var bob = await EnqueueAsync(client, "Bob", 40);

        Assert.Equal("Alice", alice.Name);
        Assert.Equal(1, alice.Position);
        Assert.Equal(2, bob.Position);
        Assert.NotEqual(Guid.Empty, alice.Id);
    }

    [Fact]
    public async Task Enqueue_EmptyName_Returns400()
    {
        var client = NewClient();
        var response = await client.PostAsJsonAsync("/api/queue", new EnqueueRequest("   ", 30));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Enqueue_InvalidAge_Returns400()
    {
        var client = NewClient();
        var response = await client.PostAsJsonAsync("/api/queue", new EnqueueRequest("Alice", -5));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---------- Get queue ----------

    [Fact]
    public async Task GetQueue_ReturnsCustomersInOrder()
    {
        var client = NewClient();
        await EnqueueAsync(client, "Alice", 30);
        await EnqueueAsync(client, "Bob", 40);

        var queue = await client.GetFromJsonAsync<List<QueueEntryDto>>("/api/queue");

        Assert.Equal(new[] { "Alice", "Bob" }, queue!.Select(e => e.Name));
        Assert.Equal(new[] { 1, 2 }, queue!.Select(e => e.Position));
    }

    // ---------- Dequeue ----------

    [Fact]
    public async Task Dequeue_ReturnsFrontCustomer()
    {
        var client = NewClient();
        await EnqueueAsync(client, "Alice", 30);
        await EnqueueAsync(client, "Bob", 40);

        var response = await client.PostAsync("/api/queue/dequeue", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var served = await response.Content.ReadFromJsonAsync<ServedCustomerDto>();
        Assert.Equal("Alice", served!.Name);

        var queue = await client.GetFromJsonAsync<List<QueueEntryDto>>("/api/queue");
        Assert.Single(queue!);
        Assert.Equal("Bob", queue![0].Name);
    }

    [Fact]
    public async Task Dequeue_EmptyQueue_Returns409()
    {
        var client = NewClient();
        var response = await client.PostAsync("/api/queue/dequeue", null);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    // ---------- Peek ----------

    [Fact]
    public async Task Peek_ReturnsFrontWithoutRemoving()
    {
        var client = NewClient();
        await EnqueueAsync(client, "Alice", 30);

        var peeked = await client.GetFromJsonAsync<QueueEntryDto>("/api/queue/peek");
        Assert.Equal("Alice", peeked!.Name);

        var queue = await client.GetFromJsonAsync<List<QueueEntryDto>>("/api/queue");
        Assert.Single(queue!);
    }

    [Fact]
    public async Task Peek_EmptyQueue_Returns404()
    {
        var client = NewClient();
        var response = await client.GetAsync("/api/queue/peek");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---------- Reverse ----------

    [Fact]
    public async Task Reverse_FirstN_ReordersQueue()
    {
        var client = NewClient();
        foreach (var name in new[] { "A", "B", "C", "D" })
            await EnqueueAsync(client, name, 30);

        var response = await client.PostAsync("/api/queue/reverse?count=3", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<ReverseResultDto>();
        Assert.Equal(3, result!.Reversed);
        Assert.Equal(new[] { "C", "B", "A", "D" }, result.Queue.Select(e => e.Name));
    }

    [Fact]
    public async Task Reverse_CountZero_IsNoOp()
    {
        var client = NewClient();
        await EnqueueAsync(client, "A", 30);
        await EnqueueAsync(client, "B", 30);

        var response = await client.PostAsync("/api/queue/reverse?count=0", null);

        var result = await response.Content.ReadFromJsonAsync<ReverseResultDto>();
        Assert.Equal(0, result!.Reversed);
        Assert.Equal(new[] { "A", "B" }, result.Queue.Select(e => e.Name));
    }

    [Fact]
    public async Task Reverse_CountGreaterThanQueue_ReversesWholeQueue()
    {
        var client = NewClient();
        await EnqueueAsync(client, "A", 30);
        await EnqueueAsync(client, "B", 30);

        var response = await client.PostAsync("/api/queue/reverse?count=99", null);

        var result = await response.Content.ReadFromJsonAsync<ReverseResultDto>();
        Assert.Equal(2, result!.Reversed);
        Assert.Equal(new[] { "B", "A" }, result.Queue.Select(e => e.Name));
    }

    [Fact]
    public async Task Reverse_NegativeCount_Returns400()
    {
        var client = NewClient();
        var response = await client.PostAsync("/api/queue/reverse?count=-1", null);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Reverse_MissingCount_Returns400()
    {
        var client = NewClient();
        var response = await client.PostAsync("/api/queue/reverse", null);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---------- Position lookup ----------

    [Fact]
    public async Task GetPosition_ReturnsEntry()
    {
        var client = NewClient();
        await EnqueueAsync(client, "Alice", 30);
        var bob = await EnqueueAsync(client, "Bob", 40);

        var entry = await client.GetFromJsonAsync<QueueEntryDto>($"/api/queue/{bob.Id}");
        Assert.Equal(2, entry!.Position);
    }

    [Fact]
    public async Task GetPosition_UnknownId_Returns404()
    {
        var client = NewClient();
        var response = await client.GetAsync($"/api/queue/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---------- Stats ----------

    [Fact]
    public async Task Stats_TrackServedAndLength()
    {
        var client = NewClient();
        await EnqueueAsync(client, "A", 30);
        await EnqueueAsync(client, "B", 30);
        await client.PostAsync("/api/queue/dequeue", null);

        var stats = await client.GetFromJsonAsync<QueueStatsDto>("/api/queue/stats");

        Assert.Equal(1, stats!.CurrentLength);
        Assert.Equal(1, stats.TotalServed);
    }

    // ---------- Infrastructure ----------

    [Fact]
    public async Task Health_ReturnsHealthy()
    {
        var client = NewClient();
        var response = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Swagger_Json_IsServed()
    {
        var client = NewClient();
        var response = await client.GetAsync("/swagger/v1/swagger.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("QueueManager API", doc.RootElement.GetProperty("info").GetProperty("title").GetString());
    }
}
