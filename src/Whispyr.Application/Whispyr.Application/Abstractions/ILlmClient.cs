namespace Whispyr.Application.Abstractions;

public interface ILlmClient
{
    Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken ct = default);
    Task<bool> ModerateAsync(string text, CancellationToken ct = default); // true => sakıncalı
}