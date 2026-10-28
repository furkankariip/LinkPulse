using LinkPulse.Api.Infrastructure;
using Microsoft.AspNetCore.Http.HttpResults;

namespace LinkPulse.Api.Features.Links;

public static class LinkEndpoints
{
    private const int MaxPageSize = 100;

    public static IEndpointRouteBuilder MapLinkEndpoints(this IEndpointRouteBuilder app)
    {
        var links = app.MapGroup("/api/links").WithTags("Links");

        links.MapPost("/", CreateLink)
            .RequireRateLimiting(RateLimitingExtensions.CreateLinkPolicy)
            .WithSummary("Shorten a URL, optionally with a custom alias and expiry date.");

        links.MapGet("/", ListLinks).WithSummary("List links, newest first.");
        links.MapGet("/{code}", GetLink).WithSummary("Get a single link.");
        links.MapGet("/{code}/stats", GetStats).WithSummary("Click analytics for a link.");
        links.MapDelete("/{code}", DeleteLink).WithSummary("Delete a link and its click history.");

        app.MapGet("/{code:minlength(3):maxlength(32)}", FollowLink)
            .WithTags("Redirect")
            .WithSummary("Redirect to the target URL and record the click.");

        return app;
    }

    private static async Task<Results<Created<LinkResponse>, ValidationProblem, ProblemHttpResult>> CreateLink(
        CreateLinkRequest request,
        HttpContext http,
        LinkService links,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        request = request.Normalize();

        var errors = CreateLinkValidator.Validate(request, clock.GetUtcNow().UtcDateTime);
        if (errors.Count > 0)
            return TypedResults.ValidationProblem(errors);

        var link = await links.CreateAsync(request, cancellationToken);
        if (link is null)
            return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: $"Alias '{request.Alias}' is already taken.");

        return TypedResults.Created($"/api/links/{link.Code}", ToResponse(link, http.Request));
    }

    private static async Task<Results<Ok<PagedResponse<LinkResponse>>, ValidationProblem>> ListLinks(
        HttpContext http,
        LinkService links,
        CancellationToken cancellationToken,
        int page = 1,
        int pageSize = 20)
    {
        if (page < 1 || pageSize is < 1 or > MaxPageSize)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["paging"] = [$"page must be >= 1 and pageSize between 1 and {MaxPageSize}."]
            });
        }

        var result = await links.ListAsync(page, pageSize, cancellationToken);
        var items = result.Items.Select(link => ToResponse(link, http.Request)).ToList();
        return TypedResults.Ok(new PagedResponse<LinkResponse>(items, result.Page, result.PageSize, result.TotalCount));
    }

    private static async Task<Results<Ok<LinkResponse>, NotFound>> GetLink(
        string code, HttpContext http, LinkService links, CancellationToken cancellationToken) =>
        await links.GetAsync(code, cancellationToken) is { } link
            ? TypedResults.Ok(ToResponse(link, http.Request))
            : TypedResults.NotFound();

    private static async Task<Results<Ok<LinkStatsResponse>, NotFound>> GetStats(
        string code, LinkService links, CancellationToken cancellationToken) =>
        await links.GetStatsAsync(code, cancellationToken) is { } stats
            ? TypedResults.Ok(stats)
            : TypedResults.NotFound();

    private static async Task<Results<NoContent, NotFound>> DeleteLink(
        string code, LinkService links, CancellationToken cancellationToken) =>
        await links.DeleteAsync(code, cancellationToken)
            ? TypedResults.NoContent()
            : TypedResults.NotFound();

    private static async Task<Results<RedirectHttpResult, NotFound, ProblemHttpResult>> FollowLink(
        string code,
        HttpContext http,
        LinkService links,
        ClickQueue clicks,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var link = await links.ResolveAsync(code, cancellationToken);
        if (link is null)
            return TypedResults.NotFound();

        var now = clock.GetUtcNow().UtcDateTime;
        if (link.IsExpiredAt(now))
            return TypedResults.Problem(statusCode: StatusCodes.Status410Gone, title: "This link has expired.");

        clicks.TryEnqueue(new ClickEvent(
            link.Id,
            now,
            ReferrerHost(http.Request.Headers.Referer),
            DeviceDetector.Detect(http.Request.Headers.UserAgent)));

        // 302 rather than 301: browsers cache permanent redirects, which would hide repeat clicks.
        return TypedResults.Redirect(link.TargetUrl);
    }

    private static LinkResponse ToResponse(LinkDetails link, HttpRequest request) => new(
        link.Code,
        $"{request.Scheme}://{request.Host}/{link.Code}",
        link.TargetUrl,
        link.CreatedAt,
        link.ExpiresAt,
        link.TotalClicks);

    private static string? ReferrerHost(string? referrer) =>
        Uri.TryCreate(referrer, UriKind.Absolute, out var uri) ? uri.Host : null;
}
