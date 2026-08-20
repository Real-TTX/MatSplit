using MatSplit.Web.Infrastructure;

namespace MatSplit.Web.Services;

/// <summary>
/// Single entry point for the MatSplit service layer. Program.cs only calls
/// <see cref="AddMatSplitServices"/>.
/// </summary>
public static class ServiceRegistration
{
    /// <summary>
    /// Registers every business service. All DbContext-backed services are
    /// Scoped; <see cref="AppConfigService"/> is a Singleton because it caches
    /// the JSON config and only depends on singletons.
    /// </summary>
    public static IServiceCollection AddMatSplitServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddHttpContextAccessor();

        // Infrastructure
        services.AddSingleton<AuditInterceptor>();

        // Configuration (cached JSON file)
        services.AddSingleton<AppConfigService>();

        // Business services
        services.AddScoped<SessionService>();
        services.AddScoped<HistoryService>();
        services.AddScoped<UserService>();
        services.AddScoped<GroupService>();
        services.AddScoped<ExpenseService>();
        services.AddScoped<PaymentService>();
        services.AddScoped<BalanceService>();
        services.AddScoped<CurrentUserService>();

        return services;
    }
}
