using Microsoft.AspNetCore.SignalR;

namespace Whispyr.Api.Hubs;

public class RoomHub : Hub
{
    public Task JoinRoom(string roomCode)
        => Groups.AddToGroupAsync(Context.ConnectionId, roomCode);

    public Task PostMessage(string roomCode, string text)
        => Clients.Group(roomCode).SendAsync("message", new {
            roomCode, text, at = DateTimeOffset.UtcNow
        });
}