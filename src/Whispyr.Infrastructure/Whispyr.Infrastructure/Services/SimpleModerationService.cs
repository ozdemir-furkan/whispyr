using System.Text.RegularExpressions;
using Whispyr.Application.Abstractions;

namespace Whispyr.Infrastructure.Services;

public sealed class SimpleModerationService : IModerationService
{
    // Küfür/uygunsuz liste (TR+EN karışık). İstediğin gibi genişletebilirsin.
    private static readonly HashSet<string> Banned = new(StringComparer.OrdinalIgnoreCase)
    {
        "amk","aq","sikerim","sikeyim","orospu","pezevenk","yarrak","göt","gotu","götünü","it oğlu",
        "şerefsiz","salak","aptal","mal","gerizekalı",
        "fuck","fucker","motherfucker","bastard","asshole","dick","cunt","bitch","whore",
        "kill you","die","rape"
    };

    // Basit link tespiti ve tekrar/bağırma kontrolü
    private static readonly Regex UrlRegex        = new(@"https?://", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex RepeatCharRegex = new(@"(.)\1{5,}",  RegexOptions.Compiled);          // aaaaaa, !!!!!!!
    private const   int   MaxLength               = 8_000;                                             // DoS/abuse
    private const   int   MaxLinks                = 3;                                                  // link spam
    private const   int   MinLenForShoutingCheck  = 8;                                                  // “OK” gibi kısa kelimeleri es geç
    private const   double ShoutingRatio          = 0.8;                                                // %80+ büyük harf

    public bool ShouldFlag(string text, out string reason)
    {
        reason = "";

        if (string.IsNullOrWhiteSpace(text))
            return false;

        // 1) Uzunluk
        if (text.Length > MaxLength)
        {
            reason = "too_long";
            return true;
        }

        // 2) Küfür / uygunsuz
        //   - Tüm listedeki öğeleri 'Contains' ile kontrol ediyoruz (küçük-büyük duyarsız).
        //   - Çok kelimeli olanlar (örn. "kill you") için de çalışır.
        foreach (var w in Banned)
        {
            if (text.Contains(w, StringComparison.OrdinalIgnoreCase))
            {
                reason = $"banned_word:{w}";
                return true;
            }
        }

        // 3) Link spam
        var linkCount = UrlRegex.Matches(text).Count;
        if (linkCount > MaxLinks)
        {
            reason = "link_spam";
            return true;
        }

        // 4) Karakter tekrarı (örn. "loooool", "!!!!!!!")
        if (RepeatCharRegex.IsMatch(text))
        {
            reason = "repeat_char_spam";
            return true;
        }

        // 5) Bağırma (çok yüksek oranda BÜYÜK HARF)
        if (text.Length >= MinLenForShoutingCheck)
        {
            int letters = 0, uppers = 0;
            foreach (var ch in text)
            {
                if (char.IsLetter(ch))
                {
                    letters++;
                    if (char.IsUpper(ch)) uppers++;
                }
            }
            if (letters >= 5 && uppers > 0 && (double)uppers / letters >= ShoutingRatio)
            {
                reason = "shouting";
                return true;
            }
        }

        // 6) Temel reklam/kimlik avı şablonları (çok kaba)
        if (text.Contains("free money", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("click here", StringComparison.OrdinalIgnoreCase))
        {
            reason = "spam_pattern";
            return true;
        }

        return false;
    }
}
