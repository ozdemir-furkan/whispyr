namespace Whispyr.Application.Prompts;

public static class SummaryPrompts
{
    public const string Version = "v1.0";
    public const string System = "You are a concise meeting/chat summarizer. Output a single short paragraph in Turkish.";
    public static string User(IEnumerable<string> lastMessages)
        => "Son konuşmaları kısa ve net özetle:\n" + string.Join("\n- ", lastMessages);
}
