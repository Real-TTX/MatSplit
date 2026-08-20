namespace MatSplit.Web.Infrastructure;

/// <summary>
/// Startup preflight for the mounted data volume.
/// The container runs as the non-root user "app" (uid 1654). Whenever another
/// container writes to the same volume as root, the app loses write access and
/// SQLite fails deep inside EF Core with the useless message
/// "SQLite Error 8: attempt to write a readonly database". This guard turns
/// that into a self-heal (empty file) or a readable error (file with content).
/// </summary>
public static class DataVolumeGuard
{
    /// <summary>
    /// Verifies that the database directory and the database file can be
    /// written. An existing but empty and unwritable file is removed so that
    /// EF Core can recreate it - deleting only needs write access on the
    /// directory, not on the file itself.
    /// </summary>
    /// <remarks>
    /// Deliberately synchronous: the file system APIs used here
    /// (Exists / Delete / open handle probe) have no async counterparts, and
    /// this runs exactly once before the host starts listening.
    /// </remarks>
    public static void EnsureDatabaseIsWritable(MatSplitPaths paths, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(logger);

        EnsureDirectoryIsWritable(paths.DatabaseDirectory);

        var file = new FileInfo(paths.DatabaseFile);
        if (!file.Exists || IsWritable(file.FullName))
        {
            return;
        }

        if (file.Length == 0)
        {
            logger.LogWarning(
                "Database file {DatabaseFile} was not writable and empty - removing it so the schema can be created. " +
                "Another container most likely created it as root on the shared volume.",
                file.FullName);

            File.Delete(file.FullName);
            return;
        }

        throw new InvalidOperationException(
            $"The database file '{file.FullName}' is not writable for user {CurrentUserName()}. " +
            "It contains data, so it is not deleted automatically. Fix the ownership on the data volume, " +
            "e.g.: docker run --rm -v <volume>:/data alpine chown -R 1654:1654 /data");
    }

    private static void EnsureDirectoryIsWritable(string directory)
    {
        var probe = Path.Combine(directory, $".write-probe-{Guid.NewGuid():N}");

        try
        {
            using (File.Create(probe))
            {
            }

            File.Delete(probe);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException(
                $"The data directory '{directory}' is not writable for user {CurrentUserName()}. " +
                "Check the ownership of the mounted volume, e.g.: " +
                "docker run --rm -v <volume>:/data alpine chown -R 1654:1654 /data",
                ex);
        }
    }

    private static bool IsWritable(string path)
    {
        try
        {
            using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite))
            {
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string CurrentUserName()
    {
        var name = Environment.UserName;
        return string.IsNullOrEmpty(name) ? "unknown" : name;
    }
}
