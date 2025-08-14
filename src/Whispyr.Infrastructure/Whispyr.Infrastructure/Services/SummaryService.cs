using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Whispyr.Application.Abstractions;
using Whispyr.Infrastructure.Data;
using Whispyr.Domain.Entities;

namespace Whispyr.Infrastructure.Services;

public class SummaryService : ISummaryService
{
    private readonly AppDbContext _db;
    private readonly ILlmClient _llm;
    private readonly ILogger<SummaryService> _logger;

    public SummaryService(AppDbContext db, ILlmClient llm, ILogger<SummaryService> logger)
    {
        _db = db;
        _llm = llm;
        _logger = logger;
    }

    private static TimeSpan Backoff(int attempt)
    {
        var baseMs = (int)Math.Min(1000 * Math.Pow(2, attempt), 15_000);
        var jitter = Random.Shared.Next(0, 500);
        return TimeSpan.FromMilliseconds(baseMs + jitter);
    }

    public async Task<string> SummarizeAsync(IEnumerable<string> texts, CancellationToken ct = default)
    {
        var prompt = BuildPrompt(texts);

        // 3 deneme
        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                var text = await _llm.SummarizeAsync(prompt, ct);
                if (!string.IsNullOrWhiteSpace(text))
                    return text;

                throw new Exception("Empty summary from LLM");
            }
            catch (OperationCanceledException) { throw; }
            // Eğer sende özel rate-limit exception türü varsa buraya ekle:
            // catch (GeminiRateLimitException ex) when (attempt < 2) { ... }
            catch (HttpRequestException ex) when (attempt < 2)
            {
                _logger.LogWarning(ex, "LLM transient (network) error, retrying...");
                await Task.Delay(Backoff(attempt), ct);
            }
            catch (Exception ex) when (attempt < 2)
            {
                _logger.LogWarning(ex, "LLM error, retrying...");
                await Task.Delay(Backoff(attempt), ct);
            }
        }

        throw new Exception("LLM failed after retries");
    }

    public async Task<SummarizeResult> CreateOrUpdateSummaryAsync(int roomId, CancellationToken ct = default)
 {
    var last50 = await _db.Messages
        .Where(m => m.RoomId == roomId && !m.IsFlagged)
        .OrderByDescending(m => m.Id)
        .Take(50)
        .ToListAsync(ct);

    if (last50.Count == 0)
        return new SummarizeResult(SummarizeStatus.NoContent);

    try
{
    var text = await SummarizeAsync(last50.Select(m => m.Text), ct);

    if (string.IsNullOrWhiteSpace(text))
        return new SummarizeResult(SummarizeStatus.UpstreamError, ErrorMessage: "Empty summary from LLM");

    var s = new RoomSummary { RoomId = roomId, Content = text, CreatedAt = DateTime.UtcNow };
    _db.RoomSummaries.Add(s);
    await _db.SaveChangesAsync(ct);

    return new SummarizeResult(SummarizeStatus.Ok, s.Id, s.CreatedAt);
}
catch (LlmRateLimitException ex)
{
    // ex.RetryAfterSeconds null ise varsayılan 5 sn verelim
    var retry = ex.RetryAfterSeconds ?? 5;
    _logger.LogWarning("LLM rate limited. retryAfter={Retry}", retry);

    return new SummarizeResult(
        SummarizeStatus.RateLimited,
        RetryAfterSeconds: retry,
        ErrorMessage: ex.Message
    );
}
catch (HttpRequestException ex)
{
    // 5xx veya ağ hatası
    _logger.LogWarning(ex, "LLM upstream error for room {RoomId}", roomId);
    return new SummarizeResult(SummarizeStatus.UpstreamError, ErrorMessage: ex.Message);
}
catch (OperationCanceledException)
{
    return new SummarizeResult(SummarizeStatus.UpstreamError, ErrorMessage: "Canceled/timeout");
}
catch (Exception ex)
 {
    _logger.LogError(ex, "Summary failed for room {RoomId}", roomId);
    return new SummarizeResult(SummarizeStatus.UpstreamError, ErrorMessage: ex.Message);
 }
 }

    private static string BuildPrompt(IEnumerable<string> texts)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Aşağıdaki sohbet mesajlarını kısa ve aksiyon odaklı bir özet halinde çıkar.");
        sb.AppendLine("Önemli kararlar, aksiyon maddeleri ve açık soruları maddeler halinde yaz.");
        sb.AppendLine();
        int i = 1;
        foreach (var t in texts.Reverse())
            sb.AppendLine($"{i++}. {t}");
        return sb.ToString();
    }
}
