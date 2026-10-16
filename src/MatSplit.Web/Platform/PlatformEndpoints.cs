// A copy of MatIdentity's src/MatIdentity.Web/Platform/PlatformEndpoints.cs (only the namespace and the caller key differ), until the shared
// package MatPlatform exists (MatDesign docs/PLATFORM.md, "Shared code"). Change it there first and copy it back, so the apps stay in step.

using System.Text.Json;

namespace MatSplit.Web.Platform;

/// <summary>What an app supplies to the platform contract; <see cref="PlatformEndpoints"/> turns it into the endpoints (docs/PLATFORM.md).</summary>
public interface IMatPlatformApp
{
    MatApiDefinition Definition(HttpContext http);

    Task<MatHealth> HealthAsync(CancellationToken ct);

    Task<IReadOnlyDictionary<string, MatStateValue>> StateAsync(CancellationToken ct);

    /// <summary>The values of the settings; a secret is <c>{ "isSet": bool }</c>, never its value.</summary>
    Task<IReadOnlyDictionary<string, object?>> ConfigurationAsync(CancellationToken ct);

    /// <summary>Saves the values that are in <paramref name="values"/> when all are valid; returns the errors per key otherwise (nothing saved).</summary>
    Task<IReadOnlyDictionary<string, string>> ConfigureAsync(IReadOnlyDictionary<string, JsonElement> values, string caller, CancellationToken ct);

    /// <summary>Runs an action of the kind <c>post</c>; null when there is no such action.</summary>
    Task<(bool Ok, string Message)?> RunActionAsync(string id, string caller, CancellationToken ct);
}

/// <summary>The endpoints of the platform contract, the same for every app: <c>/api</c>, <c>/api/health</c>, <c>/api/state</c>, <c>/api/configure</c>, <c>/api/actions/{id}</c>.</summary>
public static class PlatformEndpoints
{
    public static void MapMatPlatform(this WebApplication app, Func<EndpointFilterInvocationContext, EndpointFilterDelegate, ValueTask<object?>> requireCaller, string anonymousRateLimit)
    {
        var options = MatApiDefinition.Json;

        app.MapGet("/api", (HttpContext http, IMatPlatformApp platform) =>
        {
            var definition = platform.Definition(http);
            var body = JsonSerializer.SerializeToUtf8Bytes(definition, options);
            // The definition only changes with the version of the app: a consumer asks again cheaply.
            var etag = "\"" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(body))[..16] + "\"";
            http.Response.Headers.CacheControl = "no-cache";
            http.Response.Headers.ETag = etag;
            return http.Request.Headers.IfNoneMatch.ToString() == etag
                ? Results.StatusCode(StatusCodes.Status304NotModified)
                : Results.Bytes(body, "application/json");
        }).AllowAnonymous().RequireRateLimiting(anonymousRateLimit);

        app.MapGet("/api/health", async (IMatPlatformApp platform, CancellationToken ct) =>
        {
            var health = await platform.HealthAsync(ct);
            return Results.Json(health, options, statusCode: health.Status == "down" ? StatusCodes.Status503ServiceUnavailable : StatusCodes.Status200OK);
        }).AllowAnonymous().RequireRateLimiting(anonymousRateLimit);

        var api = app.MapGroup("/api").AllowAnonymous().AddEndpointFilter(requireCaller).DisableAntiforgery();

        api.MapGet("/state", async (IMatPlatformApp platform, CancellationToken ct) =>
            Results.Json(new { time = DateTimeOffset.Now, values = await platform.StateAsync(ct) }, options));

        api.MapGet("/state/{key}", async (string key, IMatPlatformApp platform, CancellationToken ct) =>
            (await platform.StateAsync(ct)).TryGetValue(key, out var value)
                ? Results.Json(new { key, value = value.Value, level = value.Level, text = value.Text }, options)
                : Results.Json(new { errors = new Dictionary<string, string> { [key] = "There is no such value." } }, options, statusCode: StatusCodes.Status404NotFound));

        api.MapGet("/configure", async (IMatPlatformApp platform, CancellationToken ct) =>
            Results.Json(new { values = await platform.ConfigurationAsync(ct) }, options));

        api.MapPut("/configure", async (HttpContext http, IMatPlatformApp platform, CancellationToken ct) =>
        {
            ConfigureBody? body;
            try
            {
                body = await http.Request.ReadFromJsonAsync<ConfigureBody>(options, ct);
            }
            catch (JsonException)
            {
                body = null;
            }

            if (body?.Values is null)
            {
                return Results.Json(new { errors = new Dictionary<string, string> { [string.Empty] = "Send {\"values\": {...}}." } }, options, statusCode: StatusCodes.Status400BadRequest);
            }

            var errors = await platform.ConfigureAsync(body.Values, Caller(http), ct);
            return errors.Count > 0
                ? Results.Json(new { errors }, options, statusCode: StatusCodes.Status400BadRequest)
                : Results.Json(new { values = await platform.ConfigurationAsync(ct) }, options);
        });

        api.MapPost("/actions/{id}", async (string id, HttpContext http, IMatPlatformApp platform, CancellationToken ct) =>
        {
            var result = await platform.RunActionAsync(id, Caller(http), ct);
            if (result is null)
            {
                return Results.Json(new { errors = new Dictionary<string, string> { [id] = "There is no such action." } }, options, statusCode: StatusCodes.Status404NotFound);
            }

            var (ok, message) = result.Value;
            return Results.Json(new MatActionResult(ok, message), options, statusCode: ok ? StatusCodes.Status200OK : StatusCodes.Status409Conflict);
        });
    }

    private static string Caller(HttpContext http) => http.Items[MatSplit.Web.Infrastructure.ApiAccess.CallerKey] as string ?? "api";

    private sealed record ConfigureBody(Dictionary<string, JsonElement>? Values);
}
