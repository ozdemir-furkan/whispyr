namespace Whispyr.Application.Abstractions;
public interface IModerationService
{
    bool ShouldFlag(string text, out string reason);
}
