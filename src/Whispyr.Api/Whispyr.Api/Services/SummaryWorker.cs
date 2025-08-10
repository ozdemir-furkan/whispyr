using Microsoft.EntityFrameworkCore;
using Whispyr.Infrastructure.Data;

namespace Whispyr.Api.Services;

public class SummaryWorker(IServiceProvider sp, ILogger<SummaryWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = sp.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                var since = DateTime.UtcNow.AddMinutes(-5);
                var msgs = await db.Messages
                    .AsNoTracking()
                    .Where(m => m.CreatedAt >= since && !m.IsFlagged)
                    .ToListAsync(stoppingToken);

                var grouped = msgs.GroupBy(m => m.RoomId);
                foreach (var g in grouped)
                {
                    var top5 = g
                        .OrderByDescending(x => x.Id)
                        .Take(20)
                        .Select(x => x.Text)
                        .ToList();

                    log.LogInformation("SummaryStub RoomId={RoomId} Count={Count} Samples={Samples}",
                        g.Key, g.Count(), string.Join(" | ", top5.Take(5)));
                }
            }
            catch (Exception ex)
            {
                log.LogError(ex, "SummaryWorker error");
            }

            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }
}
