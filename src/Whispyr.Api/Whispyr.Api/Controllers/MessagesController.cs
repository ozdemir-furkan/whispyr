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
    /// <summary>Belirtilen odaya mesaj oluşturur (otomatik moderasyon içerir).</summary>
    [HttpPost]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(object), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Post(string code, [FromBody] PostMessageDto dto)
    {
        if (dto is null) return BadRequest(new { error = "body_null" });
        if (string.IsNullOrWhiteSpace(dto.Text)) return BadRequest(new { error = "text_required" });

        var room = await db.Rooms.FirstOrDefaultAsync(r => r.Code == code);
        if (room is null) return NotFound();

        // --- otomatik moderasyon (tek çağrı) ---
        var isFlagged = mod.ShouldFlag(dto.Text, out var reason);

        var msg = new Message
        {
            RoomId     = room.Id,
            AuthorHash = (dto.AuthorHash ?? "").Trim(),
            Text       = dto.Text.Trim(),
            IsFlagged  = isFlagged,
            CreatedAt  = DateTime.UtcNow
        };

        db.Messages.Add(msg);
        await db.SaveChangesAsync();

        // canlı push
        await hub.Clients.Group(code).SendAsync("message", new
        {
            roomCode   = code,
            id         = msg.Id,
            text       = msg.Text,
            authorHash = msg.AuthorHash,
            at         = msg.CreatedAt,
            flagged    = msg.IsFlagged,
            reason
        });

        return Created($"/rooms/{code}/messages/{msg.Id}", new { msg.Id, msg.IsFlagged });
    }

    /// <summary>Mesajı elle bayrakla (manuel).</summary>
    [HttpPost("{id:int}/flag")] // <- mutlak değil, route tabanı altında
    public async Task<IActionResult> FlagMessage(string code, int id)
    {
        var msg = await db.Messages
            .Include(m => m.Room)
            .FirstOrDefaultAsync(m => m.Id == id && m.Room.Code == code);

        if (msg is null) return NotFound();

        msg.IsFlagged = true;
        await db.SaveChangesAsync();

        return Ok(new { msg.Id, msg.IsFlagged });
    }

    /// <summary>Bu odadaki bayraklı mesajları listele (paging).</summary>
    [HttpGet("flagged")]
    public async Task<IActionResult> ListFlagged(
    string code,
    [FromQuery] int take = 50,
    [FromQuery] int? page = 1,
    CancellationToken ct = default)
 {
    take = Math.Clamp(take, 1, 200);
    var skip = ((page ?? 1) - 1) * take;

    var room = await db.Rooms.AsNoTracking()
        .FirstOrDefaultAsync(r => r.Code == code, ct);
    if (room is null) return NotFound();

    var baseQuery = db.Messages.AsNoTracking()
        .Where(m => m.RoomId == room.Id && m.IsFlagged);

    var total = await baseQuery.CountAsync(ct);

    var items = await baseQuery
        .OrderByDescending(m => m.Id)
        .Skip(skip)
        .Take(take)
        .Select(m => new
        {
            m.Id,
            m.AuthorHash,
            m.Text,
            m.CreatedAt
        })
        .ToListAsync(ct);

    return Ok(new { total, page = page ?? 1, take, items });
 }

 [HttpPost("{id:int}/unflag")]
  public async Task<IActionResult> UnflagMessage(string code, int id)
 {
    var msg = await db.Messages
        .Include(m => m.Room)
        .FirstOrDefaultAsync(m => m.Id == id && m.Room.Code == code);

    if (msg is null) return NotFound();
    msg.IsFlagged = false;
    await db.SaveChangesAsync();
    return Ok(new { msg.Id, msg.IsFlagged });
 }

 [HttpGet("flagged/count")]
 public async Task<IActionResult> CountFlagged(string code, CancellationToken ct)
 {
    var room = await db.Rooms.AsNoTracking().FirstOrDefaultAsync(r => r.Code == code, ct);
    if (room is null) return NotFound();
    var count = await db.Messages.AsNoTracking()
        .Where(m => m.RoomId == room.Id && m.IsFlagged).CountAsync(ct);
    return Ok(new { count });
 }

}

// DTO: parametresiz sınıf
public class PostMessageDto
{
    public string Text { get; set; } = default!;
    public string? AuthorHash { get; set; }
}
