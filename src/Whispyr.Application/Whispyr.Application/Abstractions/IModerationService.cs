namespace Whispyr.Application.Abstractions;
public interface IModerationService
{
    public bool ShouldFlag(string text, out string reason)
 {
    if (text.Contains("amk", StringComparison.OrdinalIgnoreCase))
    {
        reason = "küfür";
        return true;
    }
    reason = "";
    return false;
 }
}
