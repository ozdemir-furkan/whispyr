using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Whispyr.Domain.Entities;
using Whispyr.Infrastructure.Data;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Whispyr.Application.Abstractions;

namespace Whispyr.Api.Controllers;

[ApiController]
[Route("rooms")]
public class RoomsController(AppDbContext db,ISummaryService summary) : ControllerBase
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

[HttpPost("{code}/summaries/refresh")]
[ProducesResponseType(typeof(object), StatusCodes.Status201Created)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status502BadGateway)]
public async Task<IActionResult> RefreshSummary(string code, CancellationToken ct)
{
    var room = await _db.Rooms.FirstOrDefaultAsync(r => r.Code == code, ct);
    if (room is null) return NotFound();

    if (User.GetUserId() != room.OwnerId) return Forbid();

    try
    {
        var result = await _summarizer.CreateOrUpdateSummaryAsync(room.Id, ct);

        if (result.Status == SummarizeStatus.NoContent)
            return Problem(title: "No content to summarize",
                           statusCode: StatusCodes.Status400BadRequest);

        if (result.Status == SummarizeStatus.RateLimited)
            return Problem(title: "LLM rate limited",
                           statusCode: StatusCodes.Status429TooManyRequests,
                           detail: $"Retry after {result.RetryAfterSeconds} seconds");

        if (result.Status == SummarizeStatus.UpstreamError)
            return Problem(title: "LLM upstream error",
                           statusCode: StatusCodes.Status502BadGateway,
                           detail: result.ErrorMessage);

        // OK
        return Created($"/rooms/{code}/summary", new { id = result.SummaryId, createdAt = result.CreatedAt });
    }
    catch (OperationCanceledException)
    {
        return Problem(title: "Timeout", statusCode: StatusCodes.Status504GatewayTimeout);
    }
}

[HttpGet("{code}/summary")]
public async Task<IActionResult> GetLatestSummary(string code, CancellationToken ct)
{
    var room = await db.Rooms.AsNoTracking().FirstOrDefaultAsync(r => r.Code == code, ct);
    if (room is null) return NotFound();

    var latest = await db.RoomSummaries
        .Where(s => s.RoomId == room.Id)
        .OrderByDescending(s => s.Id)
        .Select(s => new { s.Id, s.Content, s.CreatedAt })
        .FirstOrDefaultAsync(ct);

    if (latest is null) return NotFound(new { error = "no_summary" });
    return Ok(latest);
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
