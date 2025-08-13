namespace Whispyr.Application.Abstractions;

public interface ISummaryService
{
    Task<string> SummarizeAsync(IEnumerable<string> texts, CancellationToken ct = default);
    Task<SummarizeResult> CreateOrUpdateSummaryAsync(int roomId, CancellationToken ct = default);
}
