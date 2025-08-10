using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Whispyr.Domain.Entities;
using Whispyr.Infrastructure.Data;

namespace Whispyr.Api.Controllers;

[ApiController]
[Route("rooms")]
public class RoomsController(AppDbContext db) : ControllerBase
{
    static string NewCode(int len = 6)
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var rnd = Random.Shared;
        return string.Concat(Enumerable.Range(0, len).Select(_ => alphabet[rnd.Next(alphabet.Length)]));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateRoomDto dto)
    {
        // benzersiz kod üret
        string code;
        do { code = NewCode(); }
        while (await db.Rooms.AnyAsync(r => r.Code == code));

        var room = new Room { Code = code, Title = dto.Title?.Trim() ?? "" };
        db.Rooms.Add(room);
        await db.SaveChangesAsync();
        return Created($"/rooms/{room.Code}", new { room.Id, room.Code, room.Title, room.CreatedAt });
    }

    [HttpGet("{code}")]
    public async Task<IActionResult> Get(string code)
    {
        var room = await db.Rooms.AsNoTracking().FirstOrDefaultAsync(r => r.Code == code);
        return room is null ? NotFound() : Ok(room);
    }

    [HttpGet("{code}/messages")]
    public async Task<IActionResult> ListMessages(string code, [FromQuery] int take = 50, [FromQuery] long? afterId = null)
    {
        var room = await db.Rooms.AsNoTracking().FirstOrDefaultAsync(r => r.Code == code);
        if (room is null) return NotFound();

        var q = db.Messages.AsNoTracking().Where(m => m.RoomId == room.Id);
        if (afterId is not null) q = q.Where(m => m.Id > afterId.Value);
        var items = await q.OrderByDescending(m => m.Id).Take(Math.Clamp(take, 1, 200)).ToListAsync();

        return Ok(items.OrderBy(m => m.Id)); // kronolojik dön
    }
}

public record CreateRoomDto(string? Title);
