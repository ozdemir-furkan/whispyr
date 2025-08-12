using System.Net.Http.Json;
using System.Text.Json;
using Whispyr.Application.Abstractions;
using Whispyr.Application.Prompts;
using Microsoft.Extensions.Configuration;
using System.Net.Http;

namespace Whispyr.Infrastructure.Services;

public class GeminiClient : ILlmClient
{
    private readonly HttpClient _http;
    private readonly string _model;
    private readonly string _apiKey;

    public GeminiClient(HttpClient http, IConfiguration cfg)
    {
        _http = http;
        _model = cfg["Gemini:Model"] ?? "gemini-1.5-flash";
        _apiKey = cfg["Gemini:ApiKey"] ?? throw new InvalidOperationException("Gemini:ApiKey missing");
        // BaseAddress vermezsek full URL kullanacağız; timeout Program.cs'de set edilecek.
    }

    public async Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken ct = default)
    {
        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{_model}:generateContent?key={_apiKey}";

        var payload = new
        {
            contents = new[]
            {
                new {
                    role = "user",
                    parts = new object[]
                    {
                        new { text = $"[SYSTEM]\n{systemPrompt}\n[USER]\n{userPrompt}" }
                    }
                }
            },
            generationConfig = new { temperature = 0.2, maxOutputTokens = 256 },
            safetySettings = new object[] {} // varsayılan kalsın; moderasyon çıktısını biz yorumlayacağız
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(payload)
        };

        using var resp = await _http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        // Gemini yanıtı: candidates[0].content.parts[0].text
        var text = doc.RootElement
            .GetProperty("candidates")[0]
            .GetProperty("content")
            .GetProperty("parts")[0]
            .GetProperty("text")
            .GetString();

        return text ?? string.Empty;
    }

    public async Task<bool> ModerateAsync(string text, CancellationToken ct = default)
    {
        var verdict = await CompleteAsync(ModerationPrompts.System, ModerationPrompts.User(text), ct);
        verdict = (verdict ?? "").Trim().ToUpperInvariant();
        return verdict.StartsWith("FLAG");
    }
}
