using System.Net;
using System.Net.Http.Json;
using LinkPulse.Api.Features.Links;
using Microsoft.AspNetCore.Mvc;

namespace LinkPulse.Tests;

public sealed class LinkApiTests(LinkPulseFactory factory) : IClassFixture<LinkPulseFactory>
{
    private readonly HttpClient _client = factory.CreateNonRedirectingClient();

    [Fact]
    public async Task Created_link_redirects_to_target()
    {
        var link = await CreateLinkAsync("https://learn.microsoft.com/aspnet/core");

        var response = await _client.GetAsync($"/{link.Code}");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("https://learn.microsoft.com/aspnet/core", response.Headers.Location?.ToString());
        Assert.Equal(ShortCodeLength, link.Code.Length);
        Assert.EndsWith($"/{link.Code}", link.ShortUrl);
    }

    [Fact]
    public async Task Custom_alias_is_used_and_cannot_be_claimed_twice()
    {
        var link = await CreateLinkAsync("https://github.com", alias: "my-github");
        Assert.Equal("my-github", link.Code);

        var duplicate = await _client.PostAsJsonAsync("/api/links", new CreateLinkRequest("https://gitlab.com", "my-github"));

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        var problem = await duplicate.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Contains("my-github", problem!.Title);
    }

    [Theory]
    [InlineData("not a url", null, "url")]
    [InlineData("ftp://files.example.com", null, "url")]
    [InlineData("https://example.com", "a!", "alias")]
    [InlineData("https://example.com", "health", "alias")]
    public async Task Invalid_requests_return_validation_problem(string url, string? alias, string expectedField)
    {
        var response = await _client.PostAsJsonAsync("/api/links", new CreateLinkRequest(url, alias));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.Contains(expectedField, problem!.Errors.Keys);
    }

    [Fact]
    public async Task Unknown_code_returns_not_found_problem()
    {
        var response = await _client.GetAsync("/does-not-exist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Expired_link_returns_gone()
    {
        var expiresAt = factory.Clock.GetUtcNow().UtcDateTime.AddHours(1);
        var link = await CreateLinkAsync("https://example.com/promo", expiresAt: expiresAt);

        Assert.Equal(HttpStatusCode.Redirect, (await _client.GetAsync($"/{link.Code}")).StatusCode);

        factory.Clock.Advance(TimeSpan.FromHours(2));

        Assert.Equal(HttpStatusCode.Gone, (await _client.GetAsync($"/{link.Code}")).StatusCode);
    }

    [Fact]
    public async Task Stats_aggregate_clicks_by_day_referrer_and_device()
    {
        var link = await CreateLinkAsync("https://example.com/stats");

        await ClickAsync(link.Code, referrer: "https://www.linkedin.com/feed", userAgent: Desktop);
        await ClickAsync(link.Code, referrer: "https://www.linkedin.com/in/someone", userAgent: IPhone);
        await ClickAsync(link.Code, referrer: "https://news.ycombinator.com", userAgent: Desktop);

        var stats = await WaitForClicksAsync(link.Code, expected: 3);

        Assert.Equal(3, stats.TotalClicks);
        Assert.NotNull(stats.LastClickedAt);
        Assert.Equal(7, stats.Last7Days.Count);
        Assert.Equal(3, stats.Last7Days[^1].Clicks);
        Assert.Equal(new ReferrerClicks("www.linkedin.com", 2), stats.TopReferrers[0]);
        Assert.Equal(2, stats.Devices["Desktop"]);
        Assert.Equal(1, stats.Devices["Mobile"]);

        var details = await _client.GetFromJsonAsync<LinkResponse>($"/api/links/{link.Code}");
        Assert.Equal(3, details!.TotalClicks);
    }

    [Fact]
    public async Task Deleted_link_stops_resolving()
    {
        var link = await CreateLinkAsync("https://example.com/temporary");
        await _client.GetAsync($"/{link.Code}"); // warm the redirect cache

        var delete = await _client.DeleteAsync($"/api/links/{link.Code}");

        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/links/{link.Code}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/{link.Code}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.DeleteAsync($"/api/links/{link.Code}")).StatusCode);
    }

    [Fact]
    public async Task List_is_paged_and_newest_first()
    {
        await CreateLinkAsync("https://example.com/1");
        factory.Clock.Advance(TimeSpan.FromMinutes(1));
        var newest = await CreateLinkAsync("https://example.com/2");

        var page = await _client.GetFromJsonAsync<PagedResponse<LinkResponse>>("/api/links?page=1&pageSize=1");

        Assert.Single(page!.Items);
        Assert.Equal(newest.Code, page.Items[0].Code);
        Assert.True(page.TotalCount >= 2);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.GetAsync("/api/links?pageSize=500")).StatusCode);
    }

    [Fact]
    public async Task Health_endpoint_reports_healthy()
    {
        var response = await _client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    private const int ShortCodeLength = 7;
    private const string Desktop = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/130.0 Safari/537.36";
    private const string IPhone = "Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X) AppleWebKit/605.1.15 Mobile/15E148";

    private async Task<LinkResponse> CreateLinkAsync(string url, string? alias = null, DateTime? expiresAt = null)
    {
        var response = await _client.PostAsJsonAsync("/api/links", new CreateLinkRequest(url, alias, expiresAt));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<LinkResponse>())!;
    }

    private async Task ClickAsync(string code, string referrer, string userAgent)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/{code}");
        request.Headers.Referrer = new Uri(referrer);
        request.Headers.UserAgent.ParseAdd(userAgent);
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    // Clicks are persisted by a background worker, so poll briefly instead of asserting immediately.
    private async Task<LinkStatsResponse> WaitForClicksAsync(string code, int expected)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            var stats = await _client.GetFromJsonAsync<LinkStatsResponse>($"/api/links/{code}/stats");
            if (stats!.TotalClicks >= expected)
                return stats;
            await Task.Delay(50);
        }

        throw new TimeoutException($"Expected {expected} clicks for '{code}' to be recorded.");
    }
}
