using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Whispyr.Api.Hubs;
using Whispyr.Domain.Entities;
using Whispyr.Infrastructure.Data;
using Whispyr.Application.Abstractions;

namespace Whispyr.Api.Controllers;

[ApiController]
[Route("rooms/{code}/messages")]
public class MessagesController(AppDbContext db, IHubContext<RoomHub> hub, IModerationService mod) : ControllerBase
{
    /// <summary>Belirtilen oda koduna mesaj oluşturur.</summary>
    /// <param name="code">Oda kodu</param>
    /// <param name="dto">Mesaj içeriği</param>
    /// <returns>Oluşan mesajın Id’si ve flag durumu</returns>
    [ProducesResponseType(typeof(object), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [Consumes("application/json")]
    [HttpPost]
    public async Task<IActionResult> Post(string code, [FromBody] PostMessageDto dto)
    {
        if (dto is null) return BadRequest(new { error = "body_null" });
        if (string.IsNullOrWhiteSpace(dto.Text)) return BadRequest(new { error = "text_required" });

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
        var flaggedReason = "";
        if (mod.ShouldFlag(msg.Text, out var why))
          {
             msg.IsFlagged = true;
             flaggedReason = why; // <- sebebi sakla
          }  

        if (mod.ShouldFlag(msg.Text, out _)) msg.IsFlagged = true;

        db.Messages.Add(msg);
        await db.SaveChangesAsync();

        await hub.Clients.Group(code).SendAsync("message", new
        {
            roomCode = code,
            id = msg.Id,
            text = msg.Text,
            authorHash = msg.AuthorHash,
            at = msg.CreatedAt,
            flagged = msg.IsFlagged,
            reason = flaggedReason  
        });

        return Created($"/rooms/{code}/messages/{msg.Id}", new { msg.Id, msg.IsFlagged });
    }
}

// DTO: record DEĞİL, parametresiz sınıf!
public class PostMessageDto
{
    public string Text { get; set; } = default!;
    public string? AuthorHash { get; set; }
}
