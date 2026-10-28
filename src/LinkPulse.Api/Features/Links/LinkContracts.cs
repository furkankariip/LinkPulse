namespace LinkPulse.Api.Features.Links;

public sealed record CreateLinkRequest(string Url, string? Alias = null, DateTime? ExpiresAt = null)
{
    public CreateLinkRequest Normalize() => new(
        Url?.Trim() ?? string.Empty,
        string.IsNullOrWhiteSpace(Alias) ? null : Alias.Trim(),
        ExpiresAt switch
        {
            null => null,
            { Kind: DateTimeKind.Unspecified } value => DateTime.SpecifyKind(value, DateTimeKind.Utc),
            { } value => value.ToUniversalTime()
        });
}

public sealed record LinkResponse(
    string Code,
    string ShortUrl,
    string TargetUrl,
    DateTime CreatedAt,
    DateTime? ExpiresAt,
    int TotalClicks);

public sealed record LinkStatsResponse(
    string Code,
    int TotalClicks,
    DateTime? LastClickedAt,
    IReadOnlyList<DailyClicks> Last7Days,
    IReadOnlyList<ReferrerClicks> TopReferrers,
    IReadOnlyDictionary<string, int> Devices);

public sealed record DailyClicks(DateOnly Date, int Clicks);

public sealed record ReferrerClicks(string Referrer, int Clicks);

public sealed record PagedResponse<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);
