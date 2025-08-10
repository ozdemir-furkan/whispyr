using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Whispyr.Api.Hubs;
using Whispyr.Domain.Entities;
using Whispyr.Infrastructure.Data;

namespace Whispyr.Api.Controllers;

[ApiController]
[Route("rooms/{code}/messages")]
public class MessagesController(AppDbContext db, IHubContext<RoomHub> hub) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Post(string code, [FromBody] PostMessageDto dto)
    {
        var room = await db.Rooms.FirstOrDefaultAsync(r => r.Code == code);
        if (room is null) return NotFound();

        var msg = new Message
        {
            RoomId = room.Id,
            AuthorHash = (dto.AuthorHash ?? "").Trim(),
            Text = dto.Text.Trim(),
            IsFlagged = false,
            CreatedAt = DateTime.UtcNow
        };

        db.Messages.Add(msg);
        await db.SaveChangesAsync();

        // gruba canlı yayın
        await hub.Clients.Group(code).SendAsync("message", new
        {
            roomCode = code,
            id = msg.Id,
            text = msg.Text,
            authorHash = msg.AuthorHash,
            at = msg.CreatedAt
        });

        return Created($"/rooms/{code}/messages/{msg.Id}", new { msg.Id });
    }
}

public record PostMessageDto(string Text, string? AuthorHash);
