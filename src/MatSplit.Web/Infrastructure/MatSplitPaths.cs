namespace MatSplit.Web.Infrastructure;

/// <summary>
/// Central resolver for every path below the mounted data volume.
/// In the container the root is /data; on a Windows developer machine the
/// resolver falls back to &lt;contentRoot&gt;/data so that a plain
/// <c>dotnet run</c> works without additional setup.
/// </summary>
public sealed class MatSplitPaths
{
    public const string DataDirectoryEnvironmentVariable = "MATSPLIT_DATA_DIR";

    public const string DefaultLinuxDataRoot = "/data";

    private MatSplitPaths(string dataRoot, string databaseFile)
    {
        DataRoot = dataRoot;
        DatabaseFile = databaseFile;
    }

    /// <summary>Root of the persisted volume, e.g. /data.</summary>
    public string DataRoot { get; }

    /// <summary>Absolute path of the SQLite file.</summary>
    public string DatabaseFile { get; }

    public string DatabaseDirectory => Path.Combine(DataRoot, "db");

    public string ConfigDirectory => Path.Combine(DataRoot, "config");

    public string ConfigFile => Path.Combine(ConfigDirectory, "appconfig.json");

    public string ReceiptsDirectory => Path.Combine(DataRoot, "receipts");

    public string KeysDirectory => Path.Combine(DataRoot, "keys");

    public string LogsDirectory => Path.Combine(DataRoot, "logs");

    /// <summary>Connection string for Microsoft.Data.Sqlite.</summary>
    public string SqliteConnectionString => $"Data Source={DatabaseFile};Cache=Shared";

    /// <summary>
    /// Determines the data root from (1) MATSPLIT_DATA_DIR, (2) configuration
    /// key <c>Data:DataDirectory</c>, (3) the platform default.
    /// The database file can be overridden with <c>Data:DatabasePath</c>.
    /// </summary>
    public static MatSplitPaths Resolve(IConfiguration configuration, string contentRootPath)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var dataRoot = FirstNonEmpty(
            Environment.GetEnvironmentVariable(DataDirectoryEnvironmentVariable),
            configuration["Data:DataDirectory"]);

        if (string.IsNullOrWhiteSpace(dataRoot))
        {
            dataRoot = ResolveDefaultDataRoot(contentRootPath);
        }

        dataRoot = Path.GetFullPath(dataRoot);

        var databaseFile = configuration["Data:DatabasePath"];
        databaseFile = string.IsNullOrWhiteSpace(databaseFile)
            ? Path.Combine(dataRoot, "db", "matsplit.db")
            : Path.GetFullPath(ResolveRelativeToDataRoot(databaseFile, dataRoot));

        return new MatSplitPaths(dataRoot, databaseFile);
    }

    /// <summary>
    /// Creates every directory of the data volume. Called once during startup.
    /// </summary>
    public void EnsureDirectories()
    {
        Directory.CreateDirectory(DataRoot);
        Directory.CreateDirectory(DatabaseDirectory);
        Directory.CreateDirectory(ConfigDirectory);
        Directory.CreateDirectory(ReceiptsDirectory);
        Directory.CreateDirectory(KeysDirectory);
        Directory.CreateDirectory(LogsDirectory);

        var databaseDirectory = Path.GetDirectoryName(DatabaseFile);
        if (!string.IsNullOrEmpty(databaseDirectory))
        {
            Directory.CreateDirectory(databaseDirectory);
        }
    }

    /// <summary>
    /// Maps a receipt storage path (relative to the receipts root) to an
    /// absolute path and guards against directory traversal.
    /// </summary>
    public string ResolveReceiptPath(string storagePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storagePath);

        var receiptsRoot = Path.GetFullPath(ReceiptsDirectory);

        // The trailing separator matters: without it "/data/receipts-evil/x"
        // would pass the prefix test for the root "/data/receipts".
        var rootPrefix = receiptsRoot.EndsWith(Path.DirectorySeparatorChar)
            ? receiptsRoot
            : receiptsRoot + Path.DirectorySeparatorChar;

        var normalized = storagePath.Replace('\\', Path.DirectorySeparatorChar)
                                    .Replace('/', Path.DirectorySeparatorChar)
                                    .TrimStart(Path.DirectorySeparatorChar);

        var candidate = Path.GetFullPath(Path.Combine(receiptsRoot, normalized));
        if (!candidate.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Receipt path escapes the receipts directory.");
        }

        return candidate;
    }

    private static string ResolveRelativeToDataRoot(string path, string dataRoot)
    {
        return Path.IsPathRooted(path) ? path : Path.Combine(dataRoot, path);
    }

    /// <summary>
    /// Container default is /data. When that is not writable (developer machine,
    /// Windows host) the volume moves next to the solution file.
    /// </summary>
    private static string ResolveDefaultDataRoot(string contentRootPath)
    {
        if (!OperatingSystem.IsWindows() && TryCreateDirectory(DefaultLinuxDataRoot))
        {
            return DefaultLinuxDataRoot;
        }

        return ResolveDeveloperDataRoot(contentRootPath);
    }

    /// <summary>
    /// Walks up from the content root to the repository root (the folder holding
    /// the .sln / .git) and puts the volume there.
    /// <c>&lt;contentRoot&gt;/data</c> is deliberately avoided: on a
    /// case-insensitive file system it would collide with the source folder
    /// <c>src/MatSplit.Web/Data</c>.
    /// </summary>
    private static string ResolveDeveloperDataRoot(string contentRootPath)
    {
        var directory = new DirectoryInfo(contentRootPath);

        while (directory is not null)
        {
            var isRepositoryRoot = Directory.Exists(Path.Combine(directory.FullName, ".git"))
                                   || directory.EnumerateFiles("*.sln").Any();

            if (isRepositoryRoot)
            {
                return Path.Combine(directory.FullName, "data");
            }

            directory = directory.Parent;
        }

        return Path.Combine(contentRootPath, "app-data");
    }

    private static bool TryCreateDirectory(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return false;
        }
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }
}
