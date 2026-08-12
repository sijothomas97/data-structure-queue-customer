using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using QueueManager.Api;
using Xunit;

namespace QueueManager.Tests;

public class QueueHubTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public QueueHubTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Fact]
    public async Task Enqueue_BroadcastsQueueChangedToHubClients()
    {
        var app = _factory.WithWebHostBuilder(builder =>
            builder.UseSetting(TestDb.ConnectionStringKey, TestDb.NewConnectionString()));
        var client = app.CreateClient();

        var received = new TaskCompletionSource<(string Kind, int Count)>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(client.BaseAddress!, "/hubs/queue"), options =>
            {
                options.HttpMessageHandlerFactory = _ => app.Server.CreateHandler();
            })
            .Build();

        connection.On<string, List<QueueEntryDto>>("QueueChanged", (kind, queue) =>
            received.TrySetResult((kind, queue.Count)));

        await connection.StartAsync();

        var response = await client.PostAsJsonAsync("/api/queue", new EnqueueRequest("Alice", 30));
        response.EnsureSuccessStatusCode();

        var completed = await Task.WhenAny(received.Task, Task.Delay(TimeSpan.FromSeconds(10)));
        Assert.Same(received.Task, completed);

        var (kind, count) = await received.Task;
        Assert.Equal("enqueued", kind);
        Assert.Equal(1, count);
    }
}
