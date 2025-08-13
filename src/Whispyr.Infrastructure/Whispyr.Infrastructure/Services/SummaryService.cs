using Whispyr.Application.Abstractions;
using Whispyr.Application.Prompts;
using System.Net.Http.Json;

namespace Whispyr.Infrastructure.Services
{
    public class SummaryService : ISummaryService
    {
        private readonly HttpClient _httpClient;

        public SummaryService(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public async Task<string> SummarizeAsync(IEnumerable<string> texts, CancellationToken ct = default)
        {
            var inputText = string.Join("\n", texts);
            var apiKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY");

            if (string.IsNullOrWhiteSpace(apiKey))
                throw new InvalidOperationException("GEMINI_API_KEY environment variable is not set.");

            var url = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-pro:generateContent?key={apiKey}";

            var payload = new
            {
                contents = new[]
                {
                    new
                    {
                        parts = new[]
                        {
                            new { text = $"Şu metinleri özetle:\n{inputText}" }
                        }
                    }
                }
            };

            var response = await _httpClient.PostAsJsonAsync(url, payload, ct);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);

            var summary = json
                .GetProperty("candidates")[0]
                .GetProperty("content")
                .GetProperty("parts")[0]
                .GetProperty("text")
                .GetString();

            return summary ?? string.Empty;
        }
    }
}