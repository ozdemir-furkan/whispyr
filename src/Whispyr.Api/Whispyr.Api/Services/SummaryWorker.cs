using Microsoft.EntityFrameworkCore;
using Whispyr.Infrastructure.Data;
using Whispyr.Application.Abstractions;

namespace Whispyr.Api.Services;

public class SummaryWorker(IServiceProvider sp, ILogger<SummaryWorker> log) : BackgroundService
{
    private static TimeSpan Backoff(int attempt)
    {
        // 1s, 2s, 4s ... (max 15s) + küçük jitter
        var baseMs = (int)Math.Min(1000 * Math.Pow(2, attempt), 15_000);
        var jitter = Random.Shared.Next(0, 500);
        return TimeSpan.FromMilliseconds(baseMs + jitter);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = sp.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var summarizer = scope.ServiceProvider.GetRequiredService<ISummaryService>();

                // Son 5 dakikada mesaj gelen odalar
                var since = DateTime.UtcNow.AddMinutes(-5);

                var roomIds = await db.Messages
                    .AsNoTracking()
                    .Where(m => m.CreatedAt >= since && !m.IsFlagged)
                    .Select(m => m.RoomId)
                    .Distinct()
                    .ToListAsync(stoppingToken);

                foreach (var roomId in roomIds)
                {
                    // 3 deneme ile çağır
                    for (int attempt = 0; attempt < 3; attempt++)
                    {
                        try
                        {
                            var res = await summarizer.CreateOrUpdateSummaryAsync(roomId, stoppingToken);

                            if (res.Status == SummarizeStatus.Ok)
                            {
                                log.LogInformation("Summary OK room={RoomId} summaryId={SummaryId}", roomId, res.SummaryId);
                                break; // bu odayı bitirdik
                            }

                            if (res.Status == SummarizeStatus.NoContent)
                            {
                                log.LogDebug("No content to summarize for room {RoomId}", roomId);
                                break;
                            }

                            if (res.Status == SummarizeStatus.RateLimited && attempt < 2)
                            {
                                var wait = res.RetryAfterSeconds.HasValue
                                    ? TimeSpan.FromSeconds(res.RetryAfterSeconds.Value)
                                    : Backoff(attempt);
                                log.LogWarning("LLM rate-limited. room={RoomId} retry in {Wait}", roomId, wait);
                                await Task.Delay(wait, stoppingToken);
                                continue;
                            }

                            if (res.Status == SummarizeStatus.UpstreamError && attempt < 2)
                            {
                                var wait = Backoff(attempt);
                                log.LogWarning("LLM upstream error. room={RoomId} retry in {Wait}. err={Err}",
                                               roomId, wait, res.ErrorMessage);
                                await Task.Delay(wait, stoppingToken);
                                continue;
                            }

                            // başka durumlar: vazgeç
                            break;
                        }
                        catch (OperationCanceledException) { throw; }
                        catch (Exception ex) when (attempt < 2)
                        {
                            var wait = Backoff(attempt);
                            log.LogWarning(ex, "SummaryWorker transient failure room={RoomId}, retry in {Wait}", roomId, wait);
                            await Task.Delay(wait, stoppingToken);
                        }
                        catch (Exception ex)
                        {
                            log.LogError(ex, "SummaryWorker failed room={RoomId}", roomId);
                            break;
                        }
                    }
                }

                // bir sonraki tur
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                log.LogError(ex, "SummaryWorker loop error, backing off");
                // döngü toparlansın
                for (int attempt = 0; attempt < 3 && !stoppingToken.IsCancellationRequested; attempt++)
                    await Task.Delay(Backoff(attempt), stoppingToken);
            }
        }
    }
}
