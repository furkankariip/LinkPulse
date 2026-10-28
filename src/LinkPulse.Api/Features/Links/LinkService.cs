using LinkPulse.Api.Data;
using LinkPulse.Api.Domain;
using LinkPulse.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace LinkPulse.Api.Features.Links;

public sealed record LinkDetails(string Code, string TargetUrl, DateTime CreatedAt, DateTime? ExpiresAt, int TotalClicks);

public sealed record ResolvedLink(int Id, string TargetUrl, DateTime? ExpiresAt)
{
    public bool IsExpiredAt(DateTime utcNow) => ExpiresAt is { } expiresAt && expiresAt <= utcNow;
}

public sealed class LinkService(
    AppDbContext db,
    ShortCodeGenerator codeGenerator,
    IMemoryCache cache,
    TimeProvider clock)
{
    private const int MaxCodeAttempts = 5;
    private static readonly TimeSpan ResolveCacheDuration = TimeSpan.FromMinutes(10);

    /// <returns>The created link, or <c>null</c> when the requested alias is already taken.</returns>
    public async Task<LinkDetails?> CreateAsync(CreateLinkRequest request, CancellationToken cancellationToken)
    {
        var code = request.Alias ?? await GenerateUniqueCodeAsync(cancellationToken);

        if (request.Alias is not null && await db.Links.AnyAsync(l => l.Code == code, cancellationToken))
            return null;

        var link = new ShortLink
        {
            Code = code,
            TargetUrl = request.Url,
            CreatedAt = clock.GetUtcNow().UtcDateTime,
            ExpiresAt = request.ExpiresAt
        };
        db.Links.Add(link);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException) when (request.Alias is not null)
        {
            // Lost a race against a concurrent request for the same alias.
            return null;
        }

        cache.Remove(CacheKey(code));
        return new LinkDetails(link.Code, link.TargetUrl, link.CreatedAt, link.ExpiresAt, TotalClicks: 0);
    }

    public Task<LinkDetails?> GetAsync(string code, CancellationToken cancellationToken) =>
        ProjectDetails(db.Links.Where(l => l.Code == code)).FirstOrDefaultAsync(cancellationToken);

    public async Task<PagedResponse<LinkDetails>> ListAsync(int page, int pageSize, CancellationToken cancellationToken)
    {
        var totalCount = await db.Links.CountAsync(cancellationToken);
        var items = await ProjectDetails(db.Links
                .OrderByDescending(l => l.CreatedAt)
                .ThenByDescending(l => l.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize))
            .ToListAsync(cancellationToken);

        return new PagedResponse<LinkDetails>(items, page, pageSize, totalCount);
    }

    public async Task<bool> DeleteAsync(string code, CancellationToken cancellationToken)
    {
        // Clicks are removed by the ON DELETE CASCADE foreign key.
        var deleted = await db.Links.Where(l => l.Code == code).ExecuteDeleteAsync(cancellationToken);
        cache.Remove(CacheKey(code));
        return deleted > 0;
    }

    /// <summary>Hot path for redirects: served from memory after the first lookup.</summary>
    public async Task<ResolvedLink?> ResolveAsync(string code, CancellationToken cancellationToken) =>
        await cache.GetOrCreateAsync(CacheKey(code), entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = ResolveCacheDuration;
            return db.Links
                .AsNoTracking()
                .Where(l => l.Code == code)
                .Select(l => new ResolvedLink(l.Id, l.TargetUrl, l.ExpiresAt))
                .FirstOrDefaultAsync(cancellationToken);
        });

    public async Task<LinkStatsResponse?> GetStatsAsync(string code, CancellationToken cancellationToken)
    {
        var linkId = await db.Links
            .Where(l => l.Code == code)
            .Select(l => (int?)l.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (linkId is null)
            return null;

        var clicks = db.Clicks.AsNoTracking().Where(c => c.ShortLinkId == linkId);

        var totalClicks = await clicks.CountAsync(cancellationToken);
        var lastClickedAt = await clicks.MaxAsync(c => (DateTime?)c.ClickedAt, cancellationToken);

        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var windowStart = today.AddDays(-6).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var recentClicks = await clicks
            .Where(c => c.ClickedAt >= windowStart)
            .Select(c => c.ClickedAt)
            .ToListAsync(cancellationToken);
        var clicksPerDay = recentClicks
            .GroupBy(DateOnly.FromDateTime)
            .ToDictionary(g => g.Key, g => g.Count());
        var last7Days = Enumerable.Range(-6, 7)
            .Select(offset => today.AddDays(offset))
            .Select(day => new DailyClicks(day, clicksPerDay.GetValueOrDefault(day)))
            .ToList();

        var topReferrers = await clicks
            .Where(c => c.Referrer != null)
            .GroupBy(c => c.Referrer!)
            .Select(g => new { Referrer = g.Key, Clicks = g.Count() })
            .OrderByDescending(r => r.Clicks)
            .Take(5)
            .ToListAsync(cancellationToken);

        var devices = await clicks
            .GroupBy(c => c.Device)
            .Select(g => new { Device = g.Key, Clicks = g.Count() })
            .ToListAsync(cancellationToken);

        return new LinkStatsResponse(
            code,
            totalClicks,
            lastClickedAt,
            last7Days,
            topReferrers.Select(r => new ReferrerClicks(r.Referrer, r.Clicks)).ToList(),
            devices.ToDictionary(d => d.Device.ToString(), d => d.Clicks));
    }

    private async Task<string> GenerateUniqueCodeAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < MaxCodeAttempts; attempt++)
        {
            var code = codeGenerator.Next();
            if (!await db.Links.AnyAsync(l => l.Code == code, cancellationToken))
                return code;
        }

        throw new InvalidOperationException($"Could not generate a unique code after {MaxCodeAttempts} attempts.");
    }

    private static IQueryable<LinkDetails> ProjectDetails(IQueryable<ShortLink> links) =>
        links.AsNoTracking().Select(l => new LinkDetails(l.Code, l.TargetUrl, l.CreatedAt, l.ExpiresAt, l.Clicks.Count));

    private static string CacheKey(string code) => $"link:{code}";
}
