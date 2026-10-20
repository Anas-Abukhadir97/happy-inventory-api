using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace HappyInventory.Api;

internal static class RateLimitRegistration
{
    public static WebApplicationBuilder AddApiRateLimiting(this WebApplicationBuilder builder)
    {
        builder.Services.AddRateLimiter(_ => { });
        builder.Services.AddOptions<RateLimiterOptions>().Configure<IConfiguration>((options, configuration) =>
        {
            var readLimit = Limit(configuration, "RateLimits:ReadPerMinute", 120);
            var writeLimit = Limit(configuration, "RateLimits:WritePerMinute", 30);
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            {
                if (!context.Request.Path.StartsWithSegments("/api/v1"))
                    return RateLimitPartition.GetNoLimiter("outside-api");

                var read = HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method);
                var key = $"{(read ? "read" : "write")}:{ClientAddress(context)}";
                return RateLimitPartition.GetFixedWindowLimiter(key, _ => Window(read ? readLimit : writeLimit));
            });

            // Login keeps its stricter independent budget, even when the global write budget is raised.
            options.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(
                ClientAddress(context), _ => Window(10)));
            options.OnRejected = async (context, _) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                    context.HttpContext.Response.Headers.RetryAfter = Math.Max(1,
                        (int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);

                var login = context.HttpContext.Request.Path.Equals("/api/v1/auth/login", StringComparison.OrdinalIgnoreCase);
                await ApiProblemResponses.WriteAsync(context.HttpContext, 429, "rate_limited",
                    login ? "TooManyAttempts" : "RateLimitExceeded");
            };
        });
        return builder;
    }

    private static string ClientAddress(HttpContext context) => context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    private static FixedWindowRateLimiterOptions Window(int limit) => new()
    {
        PermitLimit = limit,
        Window = TimeSpan.FromMinutes(1),
        QueueLimit = 0,
        AutoReplenishment = true
    };

    private static int Limit(IConfiguration configuration, string key, int fallback)
    {
        var value = configuration.GetValue(key, fallback);
        if (value is < 1 or > 10_000)
            throw new InvalidOperationException($"{key} must be between 1 and 10000.");
        return value;
    }
}
