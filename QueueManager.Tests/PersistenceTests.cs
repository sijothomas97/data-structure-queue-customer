using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using QueueManager.Api;
using Xunit;

namespace QueueManager.Tests;

public class PersistenceTests
{
    private static WebApplicationFactory<Program> NewFactory(string connectionString) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.UseSetting(TestDb.ConnectionStringKey, connectionString));

    private static async Task EnqueueAsync(HttpClient client, string name, int age)
    {
        var response = await client.PostAsJsonAsync("/api/queue", new EnqueueRequest(name, age));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Queue_SurvivesRestart_WithOrderAndServedStats()
    {
        var connectionString = TestDb.NewConnectionString();

        // First "run" of the app: enqueue A,B,C; reverse first 2 -> B,A,C; serve B.
        await using (var factory = NewFactory(connectionString))
        {
            var client = factory.CreateClient();
            await EnqueueAsync(client, "Alice", 30);
            await EnqueueAsync(client, "Bob", 40);
            await EnqueueAsync(client, "Cara", 50);

            (await client.PostAsync("/api/queue/reverse?count=2", null)).EnsureSuccessStatusCode();

            var dequeue = await client.PostAsync("/api/queue/dequeue", null);
            dequeue.EnsureSuccessStatusCode();
            var served = await dequeue.Content.ReadFromJsonAsync<ServedCustomerDto>();
            Assert.Equal("Bob", served!.Name);
        }

        // Second "run" against the same database: state must be restored.
        await using (var factory = NewFactory(connectionString))
        {
            var client = factory.CreateClient();

            var queue = await client.GetFromJsonAsync<List<QueueEntryDto>>("/api/queue");
            Assert.Equal(new[] { "Alice", "Cara" }, queue!.Select(e => e.Name));
            Assert.Equal(new[] { 1, 2 }, queue!.Select(e => e.Position));

            var stats = await client.GetFromJsonAsync<QueueStatsDto>("/api/queue/stats");
            Assert.Equal(2, stats!.CurrentLength);
            Assert.Equal(1, stats.TotalServed);
        }
    }

    [Fact]
    public async Task EmptiedQueue_StaysEmpty_AfterRestart()
    {
        var connectionString = TestDb.NewConnectionString();

        await using (var factory = NewFactory(connectionString))
        {
            var client = factory.CreateClient();
            await EnqueueAsync(client, "Alice", 30);
            (await client.PostAsync("/api/queue/dequeue", null)).EnsureSuccessStatusCode();
        }

        await using (var factory = NewFactory(connectionString))
        {
            var client = factory.CreateClient();
            var queue = await client.GetFromJsonAsync<List<QueueEntryDto>>("/api/queue");
            Assert.Empty(queue!);
        }
    }

    [Fact]
    public async Task Metrics_ReportServedTotals_AcrossRestart()
    {
        var connectionString = TestDb.NewConnectionString();

        await using (var factory = NewFactory(connectionString))
        {
            var client = factory.CreateClient();
            await EnqueueAsync(client, "Alice", 30);
            await EnqueueAsync(client, "Bob", 40);
            (await client.PostAsync("/api/queue/dequeue", null)).EnsureSuccessStatusCode();
            (await client.PostAsync("/api/queue/dequeue", null)).EnsureSuccessStatusCode();
        }

        await using (var factory = NewFactory(connectionString))
        {
            var client = factory.CreateClient();
            var metrics = await client.GetFromJsonAsync<MetricsDto>("/api/metrics");

            Assert.Equal(0, metrics!.CurrentLength);
            Assert.Equal(2, metrics.TotalServed);
            Assert.Equal(2, metrics.ServedLastHour);
            Assert.Equal(Math.Round(2 / 60.0, 3), metrics.ThroughputPerMinuteLastHour);
            Assert.True(metrics.AverageWaitSeconds >= 0);
        }
    }

    [Fact]
    public async Task Frontend_IndexPage_IsServed()
    {
        await using var factory = NewFactory(TestDb.NewConnectionString());
        var client = factory.CreateClient();

        var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType!.MediaType);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Queue Manager", html);
        Assert.Contains("app.js", html);
    }

    [Fact]
    public async Task Frontend_SignalRClientScript_IsServed()
    {
        await using var factory = NewFactory(TestDb.NewConnectionString());
        var client = factory.CreateClient();

        var response = await client.GetAsync("/lib/signalr.min.js");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
