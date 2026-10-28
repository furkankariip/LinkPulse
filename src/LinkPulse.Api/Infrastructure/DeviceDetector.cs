using LinkPulse.Api.Domain;

namespace LinkPulse.Api.Infrastructure;

public static class DeviceDetector
{
    private static readonly string[] BotTokens = ["bot", "crawler", "spider", "curl", "wget", "python-requests", "headless"];

    public static DeviceType Detect(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent))
            return DeviceType.Unknown;

        if (ContainsAny(userAgent, BotTokens))
            return DeviceType.Bot;

        if (ContainsAny(userAgent, "ipad", "tablet") ||
            (ContainsAny(userAgent, "android") && !ContainsAny(userAgent, "mobi")))
            return DeviceType.Tablet;

        if (ContainsAny(userAgent, "mobi", "iphone", "android"))
            return DeviceType.Mobile;

        return DeviceType.Desktop;
    }

    private static bool ContainsAny(string value, params string[] tokens) =>
        tokens.Any(token => value.Contains(token, StringComparison.OrdinalIgnoreCase));
}
