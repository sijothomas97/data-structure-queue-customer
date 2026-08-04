using Microsoft.AspNetCore.SignalR;

namespace QueueManager.Api.Hubs;

/// <summary>
/// SignalR hub for live queue updates. Clients receive:
/// <list type="bullet">
///   <item><c>QueueChanged</c> — (changeKind, queue snapshot) after every mutation.</item>
/// </list>
/// </summary>
public sealed class QueueHub : Hub
{
}
