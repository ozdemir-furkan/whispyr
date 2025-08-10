using System.Text.RegularExpressions;
using Whispyr.Application.Abstractions;

namespace Whispyr.Infrastructure.Services;

public class SimpleModerationService : IModerationService
{
    static readonly string[] badWords = ["kotu1", "kotu2", "trol"]; // örnek
    static readonly Regex links = new(@"https?://", RegexOptions.IgnoreCase);

    public bool ShouldFlag(string text, out string reason)
    {
        if (badWords.Any(w => text.Contains(w, StringComparison.OrdinalIgnoreCase)))
        { reason = "bad_word"; return true; }

        if (links.Matches(text).Count > 3)
        { reason = "link_spam"; return true; }

        reason = "";
        return false;
    }
}
