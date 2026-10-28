using System.Net;
using System.Net.Http.Json;
using LinkPulse.Api.Features.Links;

namespace LinkPulse.Tests;

public sealed class RateLimitingTests(LinkPulseFactory factory) : IClassFixture<LinkPulseFactory>
{
    [Fact]
    public async Task Creating_links_is_rate_limited_per_client()
    {
        await using var limited = factory.WithWebHostBuilder(builder =>
            builder.UseSetting("RateLimiting:CreateLinkPermitLimit", "2"));
        var client = limited.CreateClient();

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 3; i++)
        {
            var response = await client.PostAsJsonAsync("/api/links", new CreateLinkRequest($"https://example.com/{i}"));
            statuses.Add(response.StatusCode);
        }

        Assert.Equal([HttpStatusCode.Created, HttpStatusCode.Created, HttpStatusCode.TooManyRequests], statuses);
    }
}
