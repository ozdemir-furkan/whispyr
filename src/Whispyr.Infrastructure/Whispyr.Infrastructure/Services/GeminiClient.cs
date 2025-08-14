// src/Whispyr.Infrastructure/Whispyr.Infrastructure/Services/GeminiClient.cs
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Whispyr.Application.Abstractions;

namespace Whispyr.Infrastructure.Services;

public sealed class GeminiClient : ILlmClient
{
    private readonly HttpClient _http;
    private readonly ILogger<GeminiClient> _log;
    private readonly string _endpoint;

    public GeminiClient(HttpClient http, ILogger<GeminiClient> log)
    {
        _http = http;
        _log  = log;

        var apiKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("GEMINI_API_KEY is not set.");

        _endpoint = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-1.5-flash:generateContent?key={apiKey}";
    }

    // --- ILlmClient ---

    public async Task<string> SummarizeAsync(string prompt, CancellationToken ct = default)
    {
        var bodyJson = BuildBodyJson([ new Part { text = prompt } ]);
        var json = await PostWithRetryAsync(bodyJson, ct);
        return ParseSummary(json);
    }

    public async Task<string> CompleteAsync(string system, string user, CancellationToken ct = default)
    {
        var bodyJson = BuildBodyJson([
            new Part { text = $"[SYSTEM]\n{system}" },
            new Part { text = $"[USER]\n{user}" }
        ]);
        var json = await PostWithRetryAsync(bodyJson, ct);
        return ParseSummary(json);
    }

    // Basit yerel moderasyon (istersen sonra LLM'e taşıyabiliriz)
    public Task<bool> ModerateAsync(string text, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text)) return Task.FromResult(true);

        string[] banned = [
            "sikerim","orospu","pezevenk","aq ","amk","fuck","fucker","motherfucker",
            "bastard","die","kill you","rape","şerefsiz","yarrak","gotu","götünü","it oğlu"
        ];

        var lower = text.ToLowerInvariant();
        foreach (var w in banned)
            if (lower.Contains(w)) return Task.FromResult(false);

        if (text.Length > 8000) return Task.FromResult(false);

        return Task.FromResult(true);
    }

    // --- HTTP & Retry ---

    // bodyJson: JSON string (her denemede yeni StringContent üretiriz)
    private async Task<string> PostWithRetryAsync(string bodyJson, CancellationToken ct)
    {
        const int maxAttempts = 4;

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, _endpoint)
            {
                Content = new StringContent(bodyJson, Encoding.UTF8, "application/json")
            };

            HttpResponseMessage resp;
            try
            {
                resp = await _http.SendAsync(req, ct);
            }
            catch (HttpRequestException ex) when (attempt < maxAttempts)
            {
                var delay = TimeSpan.FromSeconds(Math.Min(2 * attempt, 10));
                _log.LogWarning(ex, "LLM transient network error, retrying in {Delay}", delay);
                await Task.Delay(delay, ct);
                continue;
            }

            if ((int)resp.StatusCode == 429)
            {
                var retryAfter = ParseRetryAfterSeconds(resp);
                if (attempt == maxAttempts)
                    throw new LlmRateLimitException("Gemini rate limited.", retryAfter);

                var delay = TimeSpan.FromSeconds(retryAfter ?? Math.Min(2 * attempt, 10));
                _log.LogWarning("Gemini 429 (rate limit). attempt={Attempt} retryAfter={RetryAfter}s -> waiting {Delay}",
                                attempt, retryAfter, delay);
                await Task.Delay(delay, ct);
                continue;
            }

            if ((int)resp.StatusCode == 503 && attempt < maxAttempts)
            {
                var delay = TimeSpan.FromSeconds(Math.Min(2 * attempt, 10));
                _log.LogWarning("Gemini 503 (unavailable). attempt={Attempt} -> waiting {Delay}", attempt, delay);
                await Task.Delay(delay, ct);
                continue;
            }

            resp.EnsureSuccessStatusCode();
            return await resp.Content.ReadAsStringAsync(ct);
        }

        // Normalde buraya düşmez
        throw new HttpRequestException("LLM upstream error after retries.");
    }

    private static int? ParseRetryAfterSeconds(HttpResponseMessage resp)
    {
        if (resp.Headers.TryGetValues("Retry-After", out var vals))
        {
            var val = vals.FirstOrDefault();
            if (int.TryParse(val, out var s))
                return s;
        }
        return null;
    }

    // --- JSON gövdesi & parsing ---

    private static string BuildBodyJson(Part[] parts)
    {
        var payload = new
        {
            contents = new[]
            {
                new { parts }
            }
        };
        return JsonSerializer.Serialize(payload);
    }

    private static string ParseSummary(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // candidates[0].content.parts[0].text
        if (root.TryGetProperty("candidates", out var candidates) &&
            candidates.ValueKind == JsonValueKind.Array &&
            candidates.GetArrayLength() > 0)
        {
            var cand = candidates[0];
            if (cand.TryGetProperty("content", out var content) &&
                content.TryGetProperty("parts", out var parts) &&
                parts.ValueKind == JsonValueKind.Array &&
                parts.GetArrayLength() > 0)
            {
                var part0 = parts[0];
                if (part0.TryGetProperty("text", out var textEl))
                    return textEl.GetString() ?? "";
            }
        }

        return "";
    }

    private sealed class Part
    {
        public string text { get; set; } = "";
    }
}
