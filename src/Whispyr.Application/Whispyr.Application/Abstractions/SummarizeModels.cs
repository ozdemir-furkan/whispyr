namespace Whispyr.Application.Abstractions;

public enum SummarizeStatus { Ok, NoContent, RateLimited, UpstreamError }

public record SummarizeResult(
    SummarizeStatus Status,
    int? SummaryId = null,
    DateTime? CreatedAt = null,
    string? ErrorMessage = null,
    int? RetryAfterSeconds = null
);
