using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using QueueManager.Api;
using QueueManager.Api.Hubs;
using QueueManager.Api.Persistence;
using QueueManager.Domain;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("QueueDb") ?? "Data Source=data/queue.db";
QueuePersistence.EnsureDatabaseDirectory(connectionString);

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<CustomerQueue>();
builder.Services.AddDbContextFactory<QueueDbContext>(options => options.UseSqlite(connectionString));
builder.Services.AddSingleton<QueuePersistence>();
builder.Services.AddSignalR();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "QueueManager API",
        Version = "v1",
        Description = "Customer queue management: enqueue, dequeue, peek, reverse-first-N, position and wait-time tracking. Live updates via SignalR at /hubs/queue.",
    });
});

var app = builder.Build();

// Restore persisted queue state (schema is created on first run).
await app.Services.GetRequiredService<QueuePersistence>()
    .InitializeAsync(app.Services.GetRequiredService<CustomerQueue>());

app.UseSwagger();
app.UseSwaggerUI();

app.UseDefaultFiles();
app.UseStaticFiles();

// Broadcasts the current queue to all SignalR clients after a mutation.
static Task BroadcastAsync(IHubContext<QueueHub> hub, CustomerQueue queue, string changeKind) =>
    hub.Clients.All.SendAsync("QueueChanged", changeKind, queue.Snapshot().Select(QueueEntryDto.From).ToList());

var api = app.MapGroup("/api/queue").WithTags("Queue");

api.MapGet("/", (CustomerQueue queue) =>
        Results.Ok(queue.Snapshot().Select(QueueEntryDto.From).ToList()))
    .WithName("GetQueue")
    .WithSummary("Returns the full queue, front first, with positions and wait times.");

api.MapPost("/", async (EnqueueRequest request, CustomerQueue queue, QueuePersistence persistence, IHubContext<QueueHub> hub) =>
    {
        QueueEntry entry;
        try
        {
            entry = queue.Enqueue(request.Name, request.Age);
        }
        catch (ArgumentException ex)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [ex is ArgumentOutOfRangeException ? "age" : "name"] = new[] { ex.Message },
            });
        }

        await persistence.SaveQueueAsync(queue.Snapshot());
        await BroadcastAsync(hub, queue, "enqueued");
        var dto = QueueEntryDto.From(entry);
        return Results.Created($"/api/queue/{dto.Id}", dto);
    })
    .WithName("Enqueue")
    .WithSummary("Adds a customer to the back of the queue.");

api.MapPost("/dequeue", async (CustomerQueue queue, QueuePersistence persistence, TimeProvider clock, IHubContext<QueueHub> hub) =>
    {
        if (!queue.TryDequeue(out var served) || served is null)
            return Results.Conflict(new { message = "The queue is empty." });

        await persistence.SaveDequeueAsync(served, clock.GetUtcNow(), queue.Snapshot());
        await BroadcastAsync(hub, queue, "dequeued");
        return Results.Ok(ServedCustomerDto.From(served));
    })
    .WithName("Dequeue")
    .WithSummary("Serves (removes) the customer at the front of the queue.");

api.MapGet("/peek", (CustomerQueue queue) =>
    {
        var entry = queue.Peek();
        return entry is null
            ? Results.NotFound(new { message = "The queue is empty." })
            : Results.Ok(QueueEntryDto.From(entry));
    })
    .WithName("Peek")
    .WithSummary("Returns the customer at the front without removing them.");

api.MapPost("/reverse", async (int count, CustomerQueue queue, QueuePersistence persistence, IHubContext<QueueHub> hub) =>
    {
        if (count < 0)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["count"] = new[] { "Count must not be negative." },
            });
        }

        var reversed = queue.ReverseFirst(count);
        if (reversed > 1)
        {
            await persistence.SaveQueueAsync(queue.Snapshot());
            await BroadcastAsync(hub, queue, "reversed");
        }

        var snapshot = queue.Snapshot().Select(QueueEntryDto.From).ToList();
        return Results.Ok(new ReverseResultDto(count, reversed, snapshot));
    })
    .WithName("ReverseFirstN")
    .WithSummary("Reverses the order of the first N customers. N larger than the queue reverses the whole queue; N of 0 or 1 is a no-op.");

api.MapGet("/{id:guid}", (Guid id, CustomerQueue queue) =>
    {
        var entry = queue.Find(id);
        return entry is null
            ? Results.NotFound(new { message = $"Customer {id} is not in the queue." })
            : Results.Ok(QueueEntryDto.From(entry));
    })
    .WithName("GetCustomerPosition")
    .WithSummary("Returns a customer's current position and wait time.");

api.MapGet("/stats", (CustomerQueue queue) => Results.Ok(QueueStatsDto.From(queue.GetStats())))
    .WithName("GetStats")
    .WithSummary("Aggregate metrics: queue length, total served, average and longest waits.");

app.MapGet("/api/metrics", async (CustomerQueue queue, IDbContextFactory<QueueDbContext> dbFactory, TimeProvider clock) =>
    {
        var stats = queue.GetStats();
        var cutoffTicks = clock.GetUtcNow().AddHours(-1).UtcTicks;

        await using var db = await dbFactory.CreateDbContextAsync();
        var servedLastHour = await db.Served.CountAsync(s => s.ServedAtUtcTicks >= cutoffTicks);

        return Results.Ok(new MetricsDto(
            stats.CurrentLength,
            stats.TotalServed,
            Math.Round(stats.AverageWaitTime.TotalSeconds, 3),
            Math.Round(stats.LongestCurrentWait.TotalSeconds, 3),
            servedLastHour,
            Math.Round(servedLastHour / 60.0, 3)));
    })
    .WithTags("Metrics")
    .WithName("GetMetrics")
    .WithSummary("Durable throughput/wait metrics: totals survive restarts; throughput is served-per-minute over the last hour.");

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }))
    .WithTags("Health")
    .WithName("Health");

app.MapHub<QueueHub>("/hubs/queue");

app.Run();

/// <summary>Marker for WebApplicationFactory-based integration tests.</summary>
public partial class Program
{
}
