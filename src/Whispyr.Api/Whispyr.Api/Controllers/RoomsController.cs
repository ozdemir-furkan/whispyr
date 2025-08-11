using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Whispyr.Domain.Entities;
using Whispyr.Infrastructure.Data;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

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

    /// <summary>Yeni oda oluşturur.</summary>
    /// <param name="dto">Oda başlığı</param>
    /// <returns>Oda kodu ve meta</returns>
    [HttpPost]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(object), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [Authorize]
    public async Task<IActionResult> Create([FromBody] CreateRoomDto dto)
{
    if (dto is null) return BadRequest(new { error = "body_null" });
    if (string.IsNullOrWhiteSpace(dto.Title)) return BadRequest(new { error = "title_required" });

    var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
    if (string.IsNullOrEmpty(userIdStr)) return Unauthorized();
    var userId = int.Parse(userIdStr);

    var room = new Room
    {
        Code = MakeRoomCode(),
        Title = dto.Title.Trim(),
        CreatedAt = DateTime.UtcNow,
        OwnerId = userId
    };

    db.Rooms.Add(room);
    await db.SaveChangesAsync();
    return Created($"/rooms/{room.Code}", new { room.Id, room.Code, room.Title, room.OwnerId });
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
        take = Math.Clamp(take, 1, 200);

        var room = await db.Rooms.AsNoTracking().FirstOrDefaultAsync(r => r.Code == code);
        if (room is null) return NotFound();

        var query = db.Messages.AsNoTracking().Where(m => m.RoomId == room.Id);

        if (afterId is not null)
            query = query.Where(m => m.Id > afterId);

        var items = await query
            .OrderBy(m => m.Id)
            .Take(take)
            .Select(m => new {
                m.Id,
                m.RoomId,
                m.AuthorHash,
                m.Text,
                m.IsFlagged,
                m.CreatedAt
            })
            .ToListAsync();

        long? nextAfter = items.Count > 0 ? items[^1].Id : afterId;

        return Ok(new {
            items,
            paging = new {
                take,
                nextAfter
            }
        });
    }
    [HttpGet]
   [HttpGet]
    
    public async Task<IActionResult> GetRooms()
    {
        var rooms = await db.Rooms
            .AsNoTracking()
            .OrderByDescending(r => r.Id)
            .ToListAsync();

        return Ok(rooms);
    }
    [HttpDelete("{code}")]
    [Authorize]
    public async Task<IActionResult> Delete(string code)
{
    var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
    if (string.IsNullOrEmpty(userIdStr)) return Unauthorized();
    var userId = int.Parse(userIdStr);

    var room = await db.Rooms.FirstOrDefaultAsync(r => r.Code == code);
    if (room is null) return NotFound();

    if (room.OwnerId != userId) return Forbid(); // sadece owner

    db.Rooms.Remove(room);
    await db.SaveChangesAsync();
    return NoContent();
}

    private static string MakeRoomCode(int len = 6)
{
    const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    var bytes = new byte[len];
    System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);

    var sb = new System.Text.StringBuilder(len);
    for (int i = 0; i < len; i++)
        sb.Append(alphabet[bytes[i] % alphabet.Length]);
    return sb.ToString();
}
}

public record CreateRoomDto(string? Title);
