using System.Threading.RateLimiting;
using MatSplit.Web.Infrastructure;

namespace MatSplit.Web.Platform;

/// <summary>Wires the platform contract into MatSplit: the services behind it and the endpoints under <c>/api</c>.</summary>
public static class PlatformRegistration
{
    /// <summary>The rate limit of the anonymous endpoints <c>/api</c> and <c>/api/health</c>.</summary>
    public const string AnonymousRateLimit = "platform-anonymous";

    public static IServiceCollection AddMatSplitPlatform(this IServiceCollection services, MatPlatformSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        services.AddSingleton(settings);
        services.AddSingleton<ApiAccess>();
        services.AddScoped<IMatPlatformApp, SplitPlatform>();

        // Per caller address: a consumer that polls every few seconds is fine, a flood is not.
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(AnonymousRateLimit, http => RateLimitPartition.GetFixedWindowLimiter(
                http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 120, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
        });

        return services;
    }

    public static void MapMatSplitPlatform(this WebApplication app)
        => app.MapMatPlatform(ApiAccess.RequireAsync, AnonymousRateLimit);
}
