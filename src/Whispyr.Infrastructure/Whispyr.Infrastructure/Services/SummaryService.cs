using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Whispyr.Application.Abstractions;
using Whispyr.Infrastructure.Data;
using Whispyr.Domain.Entities; // RoomSummary / Message için

namespace Whispyr.Infrastructure.Services;

public class SummaryService : ISummaryService
{
    private readonly AppDbContext _db;
    private readonly ILlmClient _llm;                  // Gemini/OpenAI sarmalayıcın
    private readonly ILogger<SummaryService> _logger;

    public SummaryService(AppDbContext db, ILlmClient llm, ILogger<SummaryService> logger)
    {
        _db = db;
        _llm = llm;
        _logger = logger;
    }

    // Basit bir birleştirme + LLM çağrısı
    public async Task<string> SummarizeAsync(IEnumerable<string> texts, CancellationToken ct = default)
    {
        var prompt = BuildPrompt(texts);
        return await _llm.SummarizeAsync(prompt, ct);
    }

    // <<--- EKSİK OLAN METOT BURASI --->
    public async Task<SummarizeResult> CreateOrUpdateSummaryAsync(int roomId, CancellationToken ct = default)
    {
        // Son 50 flaglenmemiş mesajı topla
        var last50 = await _db.Messages
            .Where(m => m.RoomId == roomId && !m.IsFlagged)
            .OrderByDescending(m => m.Id)
            .Take(50)
            .ToListAsync(ct);

        if (last50.Count == 0)
            return new SummarizeResult(SummarizeStatus.NoContent);

        try
        {
            // LLM ile özet üret
            var text = await SummarizeAsync(last50.Select(m => m.Text), ct);

            if (string.IsNullOrWhiteSpace(text))
                return new SummarizeResult(SummarizeStatus.UpstreamError, ErrorMessage: "Empty summary from LLM");

            var s = new RoomSummary
            {
                RoomId = roomId,
                Content = text,
                CreatedAt = DateTime.UtcNow
            };

            _db.RoomSummaries.Add(s);
            await _db.SaveChangesAsync(ct);

            return new SummarizeResult(SummarizeStatus.Ok, s.Id, s.CreatedAt);
        }
        catch (OperationCanceledException)
        {
            // timeout iptal durumları
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
        // İstersen kendi prompt’unla değiştir
        var sb = new StringBuilder();
        sb.AppendLine("Aşağıdaki sohbet mesajlarını kısa ve aksiyon odaklı bir özet halinde çıkar.");
        sb.AppendLine("Önemli kararlar, aksiyon maddeleri ve açık soruları maddeler halinde yaz.");
        sb.AppendLine();
        int i = 1;
        foreach (var t in texts.Reverse()) // kronolojik sıraya almak istersen
            sb.AppendLine($"{i++}. {t}");
        return sb.ToString();
    }
}
