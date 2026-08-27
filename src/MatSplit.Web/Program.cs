using System.Globalization;
using MatSplit.Web.Api;
using MatSplit.Web.Data;
using MatSplit.Web.Infrastructure;
using MatSplit.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------
// Data volume: /data in the container, <contentRoot>/data on a Windows dev box.
// Override with MATSPLIT_DATA_DIR or configuration key Data:DataDirectory.
// ---------------------------------------------------------------------------
var paths = MatSplitPaths.Resolve(builder.Configuration, builder.Environment.ContentRootPath);
paths.EnsureDirectories();
builder.Services.AddSingleton(paths);

// Read the app config once up front so the cookie lifetime matches it.
var bootstrapConfig = await new AppConfigService(paths, NullLogger<AppConfigService>.Instance).GetAsync();

// ---------------------------------------------------------------------------
// Services
// ---------------------------------------------------------------------------
builder.Services.AddMatSplitServices();

builder.Services.AddDbContext<AppDbContext>((serviceProvider, options) =>
{
    options.UseSqlite(paths.SqliteConnectionString);
    options.AddInterceptors(serviceProvider.GetRequiredService<AuditInterceptor>());
});

// Keys must survive a container restart, otherwise every cookie breaks.
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(paths.KeysDirectory))
    .SetApplicationName("MatSplit");

builder.Services.AddAuthentication(MatSplitClaims.AuthenticationScheme)
    .AddCookie(MatSplitClaims.AuthenticationScheme, options =>
    {
        options.Cookie.Name = "MatSplit.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.Cookie.IsEssential = true;
        options.LoginPath = "/Account/Login";
        options.LogoutPath = "/Account/Logout";
        options.AccessDeniedPath = "/AccessDenied";
        options.ReturnUrlParameter = "returnUrl";
        options.ExpireTimeSpan = TimeSpan.FromDays(bootstrapConfig.SessionLifetimeDays);
        options.SlidingExpiration = true;

        // The json endpoints under /api answer with a status code instead of a
        // login redirect. The service worker follows redirects by default and
        // would otherwise treat the html login page as a successful sync.
        options.Events.OnRedirectToLogin = context => WriteApiStatusOrRedirect(
            context, StatusCodes.Status401Unauthorized);
        options.Events.OnRedirectToAccessDenied = context => WriteApiStatusOrRedirect(
            context, StatusCodes.Status403Forbidden);
    });

builder.Services.AddAuthorizationBuilder()
    .AddPolicy(MatSplitClaims.AdminOnlyPolicy, policy => policy
        .RequireAuthenticatedUser()
        .RequireRole(nameof(UserRole.Admin)))
    .AddPolicy(MatSplitClaims.AuthenticatedUserPolicy, policy => policy
        .RequireAuthenticatedUser()
        .RequireRole(
            nameof(UserRole.Admin),
            nameof(UserRole.User),
            nameof(UserRole.Anonymous)));

builder.Services.AddRazorPages(options =>
{
    // Everything needs a signed in user ...
    options.Conventions.AuthorizeFolder("/", MatSplitClaims.AuthenticatedUserPolicy);

    // ... the whole admin area needs the Admin role ...
    options.Conventions.AuthorizeFolder("/Admin", MatSplitClaims.AdminOnlyPolicy);

    // ... except the public entry points.
    options.Conventions.AllowAnonymousToPage("/Account/Login");
    options.Conventions.AllowAnonymousToPage("/Account/Logout");
    options.Conventions.AllowAnonymousToPage("/Join");
    options.Conventions.AllowAnonymousToPage("/View");
    options.Conventions.AllowAnonymousToPage("/Error");

    // The 403 page has to render for signed out users too, otherwise the
    // AccessDeniedPath redirect above would bounce back into the login loop.
    options.Conventions.AllowAnonymousToPage("/AccessDenied");
});

// Razor Pages binds form values with the current culture. Number inputs always
// post an invariant decimal ("30.00"), so a German thread culture would read
// that as 3000. Invariant request culture keeps model binding predictable;
// every display string formats with an explicit culture anyway.
builder.Services.Configure<RequestLocalizationOptions>(options =>
{
    options.SetDefaultCulture(CultureInfo.InvariantCulture.Name);
    options.SupportedCultures = [CultureInfo.InvariantCulture];
    options.SupportedUICultures = [CultureInfo.InvariantCulture];
    options.ApplyCurrentCultureToResponseHeaders = false;
    options.RequestCultureProviders.Clear();
});

// The framework ships English model binding messages; the whole UI is German.
builder.Services.Configure<MvcOptions>(options =>
{
    var messages = options.ModelBindingMessageProvider;

    messages.SetValueIsInvalidAccessor(
        value => $"Der Wert {value} ist ungültig.");
    messages.SetValueMustNotBeNullAccessor(
        _ => "Dieses Feld darf nicht leer sein.");
    messages.SetAttemptedValueIsInvalidAccessor(
        (value, field) => $"Der Wert '{value}' ist für {field} ungültig.");
    messages.SetNonPropertyAttemptedValueIsInvalidAccessor(
        value => $"Der Wert '{value}' ist ungültig.");
    messages.SetMissingBindRequiredValueAccessor(
        field => $"Für {field} wurde kein Wert übermittelt.");
    messages.SetMissingKeyOrValueAccessor(
        () => "Dieses Feld darf nicht leer sein.");
    messages.SetMissingRequestBodyRequiredValueAccessor(
        () => "Die Anfrage enthält keine Daten.");
    messages.SetValueMustBeANumberAccessor(
        field => $"{field} muss eine Zahl sein.");
    messages.SetNonPropertyValueMustBeANumberAccessor(
        () => "Der Wert muss eine Zahl sein.");
    messages.SetUnknownValueIsInvalidAccessor(
        field => $"Der Wert für {field} ist ungültig.");
    messages.SetNonPropertyUnknownValueIsInvalidAccessor(
        () => "Der Wert ist ungültig.");
});

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

var app = builder.Build();

// ---------------------------------------------------------------------------
// Database bootstrap: EnsureCreated + seed (no migrations in v1).
// ---------------------------------------------------------------------------
await InitializeDatabaseAsync(app);

// ---------------------------------------------------------------------------
// Pipeline
// ---------------------------------------------------------------------------
app.UseForwardedHeaders();
app.UseRequestLocalization();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
}

// 404/403 straight from the pipeline would otherwise be a blank browser page.
// The json endpoints under /api keep their bare status codes so the service
// worker can tell a failed sync from an html error page.
app.UseWhen(
    context => !context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase),
    branch => branch.UseStatusCodePagesWithReExecute("/Error", "?code={0}"));

app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseMatSplitSessionValidation();
app.UseAuthorization();

app.MapRazorPages();
app.MapSyncApi();

// Container health probe, must stay anonymous.
app.MapGet("/health", () => Results.Ok(new
{
    status = "ok",
    utc = DateTime.UtcNow,
    version = typeof(Program).Assembly.GetName().Version?.ToString() ?? "0.0.0"
}))
.AllowAnonymous()
.WithName("Health");

// Receipt images are served from the data volume, never from wwwroot.
app.MapGet("/receipts/{id:long}", async (
        long id,
        ExpenseService expenses,
        CurrentUserService currentUser,
        CancellationToken cancellationToken) =>
    {
        var file = await expenses.GetReceiptPathAsync(id, cancellationToken);
        if (file is null)
        {
            return Results.NotFound();
        }

        var groupId = file.Receipt.Expense?.GroupId ?? 0;
        if (groupId <= 0 || !await currentUser.CanViewGroupAsync(groupId, cancellationToken))
        {
            return Results.Forbid();
        }

        if (!file.Exists)
        {
            return Results.NotFound();
        }

        return Results.File(
            file.AbsolutePath,
            file.Receipt.ContentType,
            file.Receipt.FileName,
            enableRangeProcessing: true);
    })
    .RequireAuthorization(MatSplitClaims.AuthenticatedUserPolicy)
    .WithName("Receipt");

await app.RunAsync();

// Answers /api requests with a bare status code and keeps the browser redirect
// for every ordinary page request.
static Task WriteApiStatusOrRedirect(
    RedirectContext<CookieAuthenticationOptions> context,
    int statusCode)
{
    if (context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
    {
        context.Response.StatusCode = statusCode;
        return Task.CompletedTask;
    }

    context.Response.Redirect(context.RedirectUri);
    return Task.CompletedTask;
}

// Creates the schema, applies SQLite tuning, seeds the initial data and drops
// stale sessions.
static async Task InitializeDatabaseAsync(WebApplication app)
{
    using var scope = app.Services.CreateScope();
    var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("MatSplit.Startup");
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

    // Fail fast with an actionable message instead of a raw SQLite "readonly database".
    DataVolumeGuard.EnsureDatabaseIsWritable(scope.ServiceProvider.GetRequiredService<MatSplitPaths>(), logger);

    await db.Database.EnsureCreatedAsync();

    try
    {
        // WAL keeps readers (e.g. the sqlite-web dev container) from blocking writers.
        await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
        await db.Database.ExecuteSqlRawAsync("PRAGMA busy_timeout=5000;");
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
        logger.LogWarning(ex, "Could not apply SQLite pragmas");
    }

    await DbInitializer.SeedAsync(db, logger);

    var sessions = scope.ServiceProvider.GetRequiredService<SessionService>();
    await sessions.CleanupExpiredAsync();

    var paths = scope.ServiceProvider.GetRequiredService<MatSplitPaths>();
    logger.LogInformation("MatSplit data root: {DataRoot}", paths.DataRoot);
    logger.LogInformation("MatSplit database:  {DatabaseFile}", paths.DatabaseFile);
}
