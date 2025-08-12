using Whispyr.Application.Abstractions;

namespace Whispyr.Infrastructure.Services;

public class LlmModerationService : IModerationService
{
    private readonly ILlmClient _llm;
    public LlmModerationService(ILlmClient llm) => _llm = llm;

    public bool ShouldFlag(string text, out string reason)
    {
        // Basit/garanti kuralları önce uygula (ucuz):
        if (CountLinks(text) >= 4) { reason = "spam_links"; return true; }

        try
        {
            var flagged = _llm.ModerateAsync(text).GetAwaiter().GetResult();
            if (flagged) { reason = "llm_moderation"; return true; }
            reason = ""; return false;
        }
        catch
        {
            // Fail-open (geliştirmede) — prod’da fail-close tercih edebilirsin
            reason = ""; return false;
        }
    }

    private static int CountLinks(string t)
        => System.Text.RegularExpressions.Regex.Matches(t ?? "", @"https?://").Count;
}
