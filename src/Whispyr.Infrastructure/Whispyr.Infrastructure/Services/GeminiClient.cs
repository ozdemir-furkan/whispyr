using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Whispyr.Application.Abstractions;

namespace Whispyr.Infrastructure.Services;

public class GeminiClient : ILlmClient
{
    private readonly HttpClient _http;
    private readonly string _apiKey;

    public GeminiClient(IConfiguration cfg, HttpClient http)
    {
        _http = http;
        _apiKey = cfg["Gemini:ApiKey"] ?? Environment.GetEnvironmentVariable("GEMINI_API_KEY") ?? "";
    }

    // --- Summarize ---
    public async Task<string> SummarizeAsync(string prompt, CancellationToken ct = default)
    {
        var url =
            $"https://generativelanguage.googleapis.com/v1beta/models/gemini-1.5-flash:generateContent?key={_apiKey}";

        var body = new
        {
            contents = new[]
            {
                new { parts = new[] { new { text = prompt } } }
            }
        };

        using var resp = await _http.PostAsJsonAsync(url, body, ct);
        resp.EnsureSuccessStatusCode();

        using var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        var text = doc.RootElement
                      .GetProperty("candidates")[0]
                      .GetProperty("content")
                      .GetProperty("parts")[0]
                      .GetProperty("text")
                      .GetString();

        return text ?? string.Empty;
    }

    // --- Chat/Completion (basit) ---
    public async Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken ct = default)
    {
        var url =
            $"https://generativelanguage.googleapis.com/v1beta/models/gemini-1.5-flash:generateContent?key={_apiKey}";

        // Gemini'ye iki parçalı içerik veriyoruz: sistem yönergesi + kullanıcı mesajı
        var body = new
        {
            contents = new[]
            {
                new { parts = new[] { new { text = systemPrompt } } },
                new { parts = new[] { new { text = userPrompt } } }
            }
        };

        using var resp = await _http.PostAsJsonAsync(url, body, ct);
        resp.EnsureSuccessStatusCode();

        using var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        var text = doc.RootElement
                      .GetProperty("candidates")[0]
                      .GetProperty("content")
                      .GetProperty("parts")[0]
                      .GetProperty("text")
                      .GetString();

        return text ?? string.Empty;
    }

    // --- Moderasyon (şimdilik basit stub) ---
    // Dönüş: true = güvenli / izinli, false = sakıncalı
    public Task<bool> ModerateAsync(string text, CancellationToken ct = default)
    {
        // Çok basit bir yerel kontrol (link spam vb.)
        var linkCount = CountOccurrences(text, "http://") + CountOccurrences(text, "https://");
        if (linkCount >= 4) return Task.FromResult(false);

        var badWords = new[] { "kotu1", "hakaret", "küfür" };
        if (badWords.Any(w => text.Contains(w, StringComparison.OrdinalIgnoreCase)))
            return Task.FromResult(false);

        return Task.FromResult(true);
    }

    private static int CountOccurrences(string s, string needle)
    {
        if (string.IsNullOrEmpty(s) || string.IsNullOrEmpty(needle)) return 0;
        var count = 0;
        var idx = 0;
        while ((idx = s.IndexOf(needle, idx, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            count++;
            idx += needle.Length;
        }
        return count;
    }
}
