using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Whispyr.Api.Extensions;                 // GetUserId() için
using Whispyr.Application.Abstractions;
using Whispyr.Domain.Entities;
using Whispyr.Infrastructure.Data;
using System.Text; 
using System.Globalization;
using System.Text.Json;
using System.ComponentModel.DataAnnotations;

namespace Whispyr.Api.Controllers;
public record RoomInsightsDto(
    int RoomId,
    string Code,
    int TotalMessages,
    int Last24hMessages,
    int Last7dMessages,
    int FlaggedCount,
    string? TopAuthorHash,
    int? TopAuthorCount,
    DateTime? LastMessageAt,
    DateTime? LastSummaryAt
);

public record TopAuthorDto(string AuthorHash, int Count);

[ApiController]
[Produces("application/json")]
[Route("rooms")]
public class RoomsController(AppDbContext db, ISummaryService summary) : ControllerBase
{
    // --- Yardımcı: rastgele oda kodu ---
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

    /// <summary>Yeni oda oluşturur.</summary>
    [HttpPost]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(object), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [Authorize]
    public async Task<IActionResult> Create([FromBody] CreateRoomDto dto)
    {
        if (dto is null) return BadRequest(new { error = "body_null" });
        if (string.IsNullOrWhiteSpace(dto.Title)) return BadRequest(new { error = "title_required" });

        // Token’dan kullanıcıyı al
        var userIdStr = User.GetUserId();
        if (!int.TryParse(userIdStr, out var userId))
            return Forbid();

        // Odayı oluştur
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
            .Select(m => new
            {
                m.Id,
                m.RoomId,
                m.AuthorHash,
                m.Text,
                m.IsFlagged,
                m.CreatedAt
            })
            .ToListAsync();

        long? nextAfter = items.Count > 0 ? items[^1].Id : afterId;

        return Ok(new
        {
            items,
            paging = new { take, nextAfter }
        });
    }

    [HttpGet] // <- Tek attribute yeterli (dosyanda iki kez vardı, biri fazlaydı)
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
        var room = await db.Rooms.FirstOrDefaultAsync(r => r.Code == code);
        if (room is null) return NotFound();

        var userIdStr = User.GetUserId();
        if (!int.TryParse(userIdStr, out var userId))
            return Forbid();

        if (room.OwnerId is null || room.OwnerId.Value != userId)
            return Forbid();

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
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> RefreshSummary(string code, CancellationToken ct)
 {
    var room = await db.Rooms.FirstOrDefaultAsync(r => r.Code == code, ct);
    if (room is null) return NotFound();

    var userIdStr = User.GetUserId();
    if (!int.TryParse(userIdStr, out var userId)) return Forbid();
    if (room.OwnerId is null || room.OwnerId.Value != userId) return Forbid();

    try
    {
        var result = await summary.CreateOrUpdateSummaryAsync(room.Id, ct);

        if (result.Status == SummarizeStatus.NoContent)
            return Problem(title: "No content to summarize",
                           statusCode: StatusCodes.Status400BadRequest);

        if (result.Status == SummarizeStatus.RateLimited)
      {
      var retry = result.RetryAfterSeconds ?? 5;
        Response.Headers["Retry-After"] = retry.ToString();

        return Problem(
        title: "LLM rate limited",
        statusCode: StatusCodes.Status429TooManyRequests,
        detail: $"Retry after {retry} seconds"
       );
       }

        if (result.Status == SummarizeStatus.UpstreamError)
        {
            // 503 daha doğru (geçici)
            if (result.RetryAfterSeconds.HasValue)
                Response.Headers["Retry-After"] = result.RetryAfterSeconds.Value.ToString();
            return Problem(title: "LLM upstream error",
                           statusCode: StatusCodes.Status503ServiceUnavailable,
                           detail: result.ErrorMessage);
        }

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

    [HttpGet("{code}/export")]
    [Authorize]
    public async Task<IActionResult> Export(string code, [FromQuery] string? format = "json", CancellationToken ct = default)
 {
    var room = await db.Rooms.AsNoTracking().FirstOrDefaultAsync(r => r.Code == code, ct);
    if (room is null) return NotFound();

    var messages = await db.Messages.AsNoTracking()
        .Where(m => m.RoomId == room.Id)
        .OrderBy(m => m.Id)
        .Select(m => new {
            m.Id,
            m.RoomId,
            m.AuthorHash,
            m.Text,
            m.IsFlagged,
            m.CreatedAt
        })
        .ToListAsync(ct);

    if (!messages.Any())
        return NotFound(); // istersen 204 NoContent da dönebilirsin

    var safeTitle = (room.Title ?? "room").Replace(' ', '_');
    var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);

    if (string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase))
    {
        var sb = new StringBuilder();
        sb.AppendLine("Id,RoomId,AuthorHash,Text,IsFlagged,CreatedAt");
        foreach (var m in messages)
        {
            // basit CSV kaçışı
            static string esc(string? s)
                => s is null ? "" : "\"" + s.Replace("\"", "\"\"") + "\"";

            sb.Append(m.Id).Append(',')
              .Append(m.RoomId).Append(',')
              .Append(esc(m.AuthorHash)).Append(',')
              .Append(esc(m.Text)).Append(',')
              .Append(m.IsFlagged ? "true" : "false").Append(',')
              .Append(m.CreatedAt.ToString("o", CultureInfo.InvariantCulture))
              .AppendLine();
        }

        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        var fn = $"transcript_{safeTitle}_{timestamp}.csv";
        return File(bytes, "text/csv; charset=utf-8", fn);
    }
    else
    {
        var json = JsonSerializer.Serialize(messages, new JsonSerializerOptions { WriteIndented = true });
        var bytes = Encoding.UTF8.GetBytes(json);
        var fn = $"transcript_{safeTitle}_{timestamp}.json";
        return File(bytes, "application/json; charset=utf-8", fn);
    }
 }

// (Opsiyonel) Basit arama:
    [HttpGet("{code}/messages/search")]
    public async Task<IActionResult> SearchMessages(string code, [FromQuery] string q, [FromQuery] int take = 50, CancellationToken ct = default)
  {
    if (string.IsNullOrWhiteSpace(q))
        return BadRequest(new { error = "q_required" });

    var room = await db.Rooms.AsNoTracking().FirstOrDefaultAsync(r => r.Code == code, ct);
    if (room is null) return NotFound();

    take = Math.Clamp(take, 1, 500);

    var items = await db.Messages.AsNoTracking()
        .Where(m => m.RoomId == room.Id && m.Text.Contains(q))
        .OrderBy(m => m.Id)
        .Take(take)
        .Select(m => new { m.Id, m.Text, m.CreatedAt, m.IsFlagged })
        .ToListAsync(ct);

    return Ok(new { count = items.Count, items });
  }

  [HttpGet("{code}/insights")]
  public async Task<IActionResult> GetInsights(string code, CancellationToken ct)
 {
    var now  = DateTime.UtcNow;
    var dt24 = now.AddHours(-24);
    var dt7d = now.AddDays(-7);

    var room = await db.Rooms.AsNoTracking().FirstOrDefaultAsync(r => r.Code == code, ct);
    if (room is null) return NotFound();

    // Hepsini SIRAYLA bekle (aynı DbContext üstünde paralel yok!)
    var baseQuery = db.Messages.AsNoTracking().Where(m => m.RoomId == room.Id);

    var totalMessages   = await baseQuery.CountAsync(ct);
    var last24hMessages = await baseQuery.Where(m => m.CreatedAt >= dt24 && !m.IsFlagged).CountAsync(ct);
    var last7dMessages  = await baseQuery.Where(m => m.CreatedAt >= dt7d  && !m.IsFlagged).CountAsync(ct);
    var flaggedCount    = await baseQuery.Where(m => m.IsFlagged).CountAsync(ct);

    var lastMessageAt = await baseQuery.OrderByDescending(m => m.CreatedAt)
                                       .Select(m => (DateTime?)m.CreatedAt)
                                       .FirstOrDefaultAsync(ct);

    var lastSummaryAt = await db.RoomSummaries.AsNoTracking()
                            .Where(s => s.RoomId == room.Id)
                            .OrderByDescending(s => s.CreatedAt)
                            .Select(s => (DateTime?)s.CreatedAt)
                            .FirstOrDefaultAsync(ct);

    var top = await baseQuery.Where(m => m.CreatedAt >= dt7d && m.AuthorHash != null)
                             .GroupBy(m => m.AuthorHash!)
                             .Select(g => new { AuthorHash = g.Key, Count = g.Count() })
                             .OrderByDescending(x => x.Count)
                             .FirstOrDefaultAsync(ct);

    var topAuthorHash  = string.IsNullOrWhiteSpace(top?.AuthorHash) ? null : top!.AuthorHash;
    int? topAuthorCount = top?.Count;

    return Ok(new RoomInsightsDto(
        RoomId: room.Id,
        Code: room.Code,
        TotalMessages: totalMessages,
        Last24hMessages: last24hMessages,
        Last7dMessages: last7dMessages,
        FlaggedCount: flaggedCount,
        TopAuthorHash: topAuthorHash,
        TopAuthorCount: topAuthorCount,
        LastMessageAt: lastMessageAt,
        LastSummaryAt: lastSummaryAt
    ));
 } 

 [HttpGet("{code}/summaries")]
 public async Task<IActionResult> ListSummaries(
    string code,
    [FromQuery] int take = 20,
    [FromQuery] int? page = 1,
    CancellationToken ct = default)
 {
    take = Math.Clamp(take, 1, 100);
    var skip = ((page ?? 1) - 1) * take;

    var room = await db.Rooms.AsNoTracking().FirstOrDefaultAsync(r => r.Code == code, ct);
    if (room is null) return NotFound();

    var total = await db.RoomSummaries.AsNoTracking()
        .CountAsync(s => s.RoomId == room.Id, ct);

    var items = await db.RoomSummaries.AsNoTracking()
        .Where(s => s.RoomId == room.Id)
        .OrderByDescending(s => s.Id)
        .Skip(skip)
        .Take(take)
        .Select(s => new { s.Id, s.Content, s.CreatedAt })
        .ToListAsync(ct);

    return Ok(new { total, page = page ?? 1, take, items });
  }

  [HttpGet("{code}/summaries/{id:int}")]
  public async Task<IActionResult> GetSummaryById(string code, int id, CancellationToken ct = default)
 {
    // Odayı doğrula
    var room = await db.Rooms
        .AsNoTracking()
        .FirstOrDefaultAsync(r => r.Code == code, ct);
    if (room is null) return NotFound();

    // İstenen özeti getir
    var summary = await db.RoomSummaries
        .AsNoTracking()
        .Where(s => s.RoomId == room.Id && s.Id == id)
        .Select(s => new { s.Id, s.Content, s.CreatedAt })
        .FirstOrDefaultAsync(ct);

    if (summary is null) return NotFound();
    return Ok(summary);
 }

 [HttpDelete("{code}/summaries/{id:int}")]
 [Authorize]
 public async Task<IActionResult> DeleteSummary(string code, int id, CancellationToken ct = default)
 {
    var room = await db.Rooms.FirstOrDefaultAsync(r => r.Code == code, ct);
    if (room is null) return NotFound();

    // sadece oda sahibi silebilsin
    var userIdStr = User.GetUserId();
    if (!int.TryParse(userIdStr, out var userId)) return Forbid();
    if (room.OwnerId is null || room.OwnerId.Value != userId) return Forbid();

    var entity = await db.RoomSummaries.FirstOrDefaultAsync(s => s.RoomId == room.Id && s.Id == id, ct);
    if (entity is null) return NotFound();

    db.RoomSummaries.Remove(entity);
    await db.SaveChangesAsync(ct);
    return NoContent();
 }

 [HttpDelete("{code}/summaries")]
 [Authorize]
 public async Task<IActionResult> DeleteSummaries(
    string code,
    [FromQuery] DateTime? olderThan = null,
    CancellationToken ct = default)
 {
    var room = await db.Rooms.FirstOrDefaultAsync(r => r.Code == code, ct);
    if (room is null) return NotFound();

    var userIdStr = User.GetUserId();
    if (!int.TryParse(userIdStr, out var userId)) return Forbid();
    if (room.OwnerId is null || room.OwnerId.Value != userId) return Forbid();

    var q = db.RoomSummaries.Where(s => s.RoomId == room.Id);
    if (olderThan is not null)
        q = q.Where(s => s.CreatedAt < olderThan.Value);

    var toDelete = await q.ToListAsync(ct);
    if (toDelete.Count == 0) return NoContent();

    db.RoomSummaries.RemoveRange(toDelete);
    await db.SaveChangesAsync(ct);
    return NoContent();
 }

 [HttpPatch("{code}")]
 [Authorize]
 public async Task<IActionResult> Update(string code, [FromBody] UpdateRoomDto dto, CancellationToken ct)
 {
    if (dto is null) return BadRequest(new { error = "body_null" });

    var room = await db.Rooms.FirstOrDefaultAsync(r => r.Code == code, ct);
    if (room is null) return NotFound();

    var userIdStr = User.GetUserId();
    if (!int.TryParse(userIdStr, out var userId)) return Forbid();
    if (room.OwnerId is null || room.OwnerId.Value != userId) return Forbid();

    

   
    if (string.IsNullOrWhiteSpace(dto.Title)) return BadRequest(new { error = "title_required" });
    room.Title = dto.Title.Trim();

    await db.SaveChangesAsync(ct);
    return NoContent();
 }


}

public record CreateRoomDto(string? Title);
public record UpdateRoomDto([Required] string Title);