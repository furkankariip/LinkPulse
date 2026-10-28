using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.Extensions.Options;

namespace LinkPulse.Api.Infrastructure;

public sealed class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    public int CreateLinkPermitLimit { get; set; } = 10;
    public int WindowSeconds { get; set; } = 60;
}

public static class RateLimitingExtensions
{
    public const string CreateLinkPolicy = "create-link";

    public static IServiceCollection AddLinkRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<RateLimitingOptions>(configuration.GetSection(RateLimitingOptions.SectionName));

        return services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limiter.OnRejected = (context, _) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter =
                        ((int)retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
                }
                return ValueTask.CompletedTask;
            };

            limiter.AddPolicy(CreateLinkPolicy, http =>
            {
                var options = http.RequestServices.GetRequiredService<IOptions<RateLimitingOptions>>().Value;
                var client = http.Connection.RemoteIpAddress?.ToString() ?? "unknown";

                return RateLimitPartition.GetFixedWindowLimiter(client, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = options.CreateLinkPermitLimit,
                    Window = TimeSpan.FromSeconds(options.WindowSeconds),
                    QueueLimit = 0
                });
            });
        });
    }
}
