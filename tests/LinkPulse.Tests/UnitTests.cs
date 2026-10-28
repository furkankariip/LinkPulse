using LinkPulse.Api.Domain;
using LinkPulse.Api.Features.Links;
using LinkPulse.Api.Infrastructure;

namespace LinkPulse.Tests;

public sealed class ShortCodeGeneratorTests
{
    private readonly ShortCodeGenerator _generator = new();

    [Fact]
    public void Generates_base62_codes_of_default_length()
    {
        var code = _generator.Next();

        Assert.Equal(ShortCodeGenerator.DefaultLength, code.Length);
        Assert.All(code, c => Assert.Contains(c, ShortCodeGenerator.Alphabet));
    }

    [Fact]
    public void Codes_are_effectively_unique()
    {
        var codes = Enumerable.Range(0, 10_000).Select(_ => _generator.Next()).ToHashSet();

        Assert.Equal(10_000, codes.Count);
    }
}

public sealed class DeviceDetectorTests
{
    [Theory]
    [InlineData(null, DeviceType.Unknown)]
    [InlineData("", DeviceType.Unknown)]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/130.0", DeviceType.Desktop)]
    [InlineData("Mozilla/5.0 (Macintosh; Intel Mac OS X 14_0) Safari/605.1.15", DeviceType.Desktop)]
    [InlineData("Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X) Mobile/15E148", DeviceType.Mobile)]
    [InlineData("Mozilla/5.0 (Linux; Android 14; Pixel 8) Chrome/130.0 Mobile Safari/537.36", DeviceType.Mobile)]
    [InlineData("Mozilla/5.0 (iPad; CPU OS 17_0 like Mac OS X)", DeviceType.Tablet)]
    [InlineData("Mozilla/5.0 (Linux; Android 14; SM-X710) Chrome/130.0 Safari/537.36", DeviceType.Tablet)]
    [InlineData("Mozilla/5.0 (compatible; Googlebot/2.1; +http://www.google.com/bot.html)", DeviceType.Bot)]
    [InlineData("curl/8.4.0", DeviceType.Bot)]
    public void Classifies_user_agents(string? userAgent, DeviceType expected) =>
        Assert.Equal(expected, DeviceDetector.Detect(userAgent));
}

public sealed class CreateLinkValidatorTests
{
    private static readonly DateTime Now = new(2026, 3, 15, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData("https://example.com")]
    [InlineData("http://localhost:5000/path?query=1#hash")]
    public void Accepts_valid_urls(string url) =>
        Assert.Empty(CreateLinkValidator.Validate(new CreateLinkRequest(url), Now));

    [Theory]
    [InlineData("")]
    [InlineData("example.com")]
    [InlineData("javascript:alert(1)")]
    [InlineData("mailto:someone@example.com")]
    public void Rejects_non_http_urls(string url) =>
        Assert.Contains("url", CreateLinkValidator.Validate(new CreateLinkRequest(url), Now).Keys);

    [Theory]
    [InlineData("ab")]
    [InlineData("has space")]
    [InlineData("API")]
    [InlineData("scalar")]
    public void Rejects_invalid_or_reserved_aliases(string alias) =>
        Assert.Contains("alias", CreateLinkValidator.Validate(new CreateLinkRequest("https://example.com", alias), Now).Keys);

    [Fact]
    public void Rejects_expiry_in_the_past() =>
        Assert.Contains("expiresAt", CreateLinkValidator.Validate(new CreateLinkRequest("https://example.com", ExpiresAt: Now.AddMinutes(-1)), Now).Keys);

    [Fact]
    public void Normalize_trims_input_and_treats_blank_alias_as_missing()
    {
        var request = new CreateLinkRequest("  https://example.com  ", "   ", new DateTime(2026, 4, 1)).Normalize();

        Assert.Equal("https://example.com", request.Url);
        Assert.Null(request.Alias);
        Assert.Equal(DateTimeKind.Utc, request.ExpiresAt!.Value.Kind);
    }
}
