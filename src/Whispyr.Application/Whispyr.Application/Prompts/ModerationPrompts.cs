namespace Whispyr.Application.Prompts;

public static class ModerationPrompts
{
    public const string Version = "v1.0";
    public const string System = """
You are a strict text moderation engine. Output EXACTLY one token: OK or FLAG.
Flag if content includes: hate/harassment, threats/violence, sexual minors, self-harm, personal data (PII), illegal activities, or spam (≥4 links or repeated text).
""";
    public static string User(string text) => $"TEXT:\n{text}\nAnswer with ONLY: OK or FLAG.";
}