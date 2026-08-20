using System.Text.Json;
using MatSplit.Web.Infrastructure;
using MatSplit.Web.Services.Models;

namespace MatSplit.Web.Services;

/// <summary>
/// Reads and writes /data/config/appconfig.json. The file is created with
/// defaults on first access. The current value is cached in memory, so Razor
/// Pages can call <see cref="Current"/> without hitting the disk.
/// Registered as Singleton (only depends on singletons).
/// </summary>
public sealed class AppConfigService(MatSplitPaths paths, ILogger<AppConfigService> logger)
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly SemaphoreSlim gate = new(1, 1);

    private AppConfig? cached;

    /// <summary>
    /// Cached configuration. Falls back to defaults until the file has been
    /// loaded once (which startup does).
    /// </summary>
    public AppConfig Current => cached ?? new AppConfig();

    /// <summary>
    /// Loads the configuration, creating the file with defaults when missing.
    /// </summary>
    public async Task<AppConfig> GetAsync(CancellationToken cancellationToken = default)
    {
        if (cached is not null)
        {
            return cached;
        }

        await gate.WaitAsync(cancellationToken);
        try
        {
            if (cached is not null)
            {
                return cached;
            }

            cached = await LoadOrCreateAsync(cancellationToken);
            return cached;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// Persists the configuration and refreshes the cache.
    /// </summary>
    public async Task<Result> SaveAsync(AppConfig config, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);

        var normalized = config.Normalized();

        await gate.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(paths.ConfigDirectory);
            var json = JsonSerializer.Serialize(normalized, SerializerOptions);
            await File.WriteAllTextAsync(paths.ConfigFile, json, cancellationToken);
            cached = normalized;
            return Result.Ok();
        }
        catch (IOException ex)
        {
            logger.LogError(ex, "Could not write app config to {Path}", paths.ConfigFile);
            return Result.Fail("Die Konfiguration konnte nicht gespeichert werden.");
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogError(ex, "No permission to write app config to {Path}", paths.ConfigFile);
            return Result.Fail("Keine Schreibrechte für die Konfigurationsdatei.");
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Drops the cache so the next read hits the file again.</summary>
    public void InvalidateCache() => cached = null;

    private async Task<AppConfig> LoadOrCreateAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(paths.ConfigDirectory);

        if (!File.Exists(paths.ConfigFile))
        {
            var defaults = new AppConfig().Normalized();
            await WriteAsync(defaults, cancellationToken);
            logger.LogInformation("Created default app config at {Path}", paths.ConfigFile);
            return defaults;
        }

        try
        {
            var json = await File.ReadAllTextAsync(paths.ConfigFile, cancellationToken);
            var parsed = JsonSerializer.Deserialize<AppConfig>(json, SerializerOptions);
            if (parsed is null)
            {
                logger.LogWarning("App config at {Path} was empty, using defaults", paths.ConfigFile);
                return new AppConfig().Normalized();
            }

            return parsed.Normalized();
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "App config at {Path} is not valid JSON, using defaults", paths.ConfigFile);
            return new AppConfig().Normalized();
        }
    }

    private async Task WriteAsync(AppConfig config, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(config, SerializerOptions);
        await File.WriteAllTextAsync(paths.ConfigFile, json, cancellationToken);
    }
}
