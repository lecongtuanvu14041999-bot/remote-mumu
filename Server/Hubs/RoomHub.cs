using Microsoft.AspNetCore.SignalR;

namespace Server.Hubs;

public sealed class RoomHub : Hub
{
    public async Task JoinRoom(string roomId, string displayName)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, roomId);
        await Clients.Group(roomId).SendAsync("MemberJoined", new { displayName, connectionId = Context.ConnectionId });
    }

    public async Task SendControl(string roomId, object control)
    {
        await Clients.OthersInGroup(roomId).SendAsync("ControlReceived", control);
    }

    public async Task SendFrame(string roomId, string deviceId, string imageBase64)
    {
        await Clients.OthersInGroup(roomId).SendAsync("FrameReceived", new { deviceId, imageBase64 });
    }

    public async Task SendChat(string roomId, string displayName, string message)
    {
        await Clients.Group(roomId).SendAsync("ChatReceived", new { displayName, message, sentAt = DateTimeOffset.UtcNow });
    }

    public async Task SetController(string roomId, string connectionId)
    {
        await Clients.Group(roomId).SendAsync("ControllerChanged", connectionId);
    }
}
