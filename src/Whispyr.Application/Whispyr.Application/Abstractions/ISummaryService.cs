namespace Whispyr.Application.Abstractions;

public interface ISummaryService
{
    Task<string> SummarizeAsync(IEnumerable<string> texts, CancellationToken ct = default);
}
