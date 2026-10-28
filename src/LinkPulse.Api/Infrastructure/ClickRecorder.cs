using LinkPulse.Api.Data;
using LinkPulse.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace LinkPulse.Api.Infrastructure;

public sealed class ClickRecorder(
    ClickQueue queue,
    IServiceScopeFactory scopeFactory,
    ILogger<ClickRecorder> logger) : BackgroundService
{
    private const int MaxBatchSize = 100;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var batch = new List<ClickEvent>(MaxBatchSize);

        while (await queue.Reader.WaitToReadAsync(stoppingToken))
        {
            while (batch.Count < MaxBatchSize && queue.Reader.TryRead(out var click))
                batch.Add(click);

            try
            {
                await SaveBatchAsync(batch, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Failed to persist a batch of {Count} clicks", batch.Count);
            }

            batch.Clear();
        }
    }

    private async Task SaveBatchAsync(List<ClickEvent> batch, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // A link may have been deleted between the redirect and this write.
        var linkIds = batch.Select(c => c.LinkId).Distinct().ToList();
        var existing = await db.Links
            .Where(l => linkIds.Contains(l.Id))
            .Select(l => l.Id)
            .ToHashSetAsync(cancellationToken);

        db.Clicks.AddRange(batch
            .Where(c => existing.Contains(c.LinkId))
            .Select(c => new LinkClick
            {
                ShortLinkId = c.LinkId,
                ClickedAt = c.ClickedAt,
                Referrer = c.Referrer,
                Device = c.Device
            }));

        await db.SaveChangesAsync(cancellationToken);
    }
}
