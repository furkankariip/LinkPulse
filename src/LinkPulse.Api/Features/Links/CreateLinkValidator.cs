using System.Text.RegularExpressions;

namespace LinkPulse.Api.Features.Links;

public static partial class CreateLinkValidator
{
    // Aliases share the root path with these routes, so they can never be claimed.
    private static readonly HashSet<string> ReservedAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        "api", "health", "openapi", "scalar", "index.html"
    };

    [GeneratedRegex("^[A-Za-z0-9_-]{3,32}$")]
    private static partial Regex AliasPattern();

    public static Dictionary<string, string[]> Validate(CreateLinkRequest request, DateTime utcNow)
    {
        var errors = new Dictionary<string, string[]>();

        if (!Uri.TryCreate(request.Url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            errors["url"] = ["Url must be an absolute http or https address."];
        }
        else if (request.Url.Length > 2048)
        {
            errors["url"] = ["Url must be 2048 characters or fewer."];
        }

        if (request.Alias is { } alias)
        {
            if (!AliasPattern().IsMatch(alias))
                errors["alias"] = ["Alias must be 3-32 characters: letters, digits, '-' or '_'."];
            else if (ReservedAliases.Contains(alias))
                errors["alias"] = [$"'{alias}' is reserved."];
        }

        if (request.ExpiresAt is { } expiresAt && expiresAt <= utcNow)
            errors["expiresAt"] = ["ExpiresAt must be in the future."];

        return errors;
    }
}
