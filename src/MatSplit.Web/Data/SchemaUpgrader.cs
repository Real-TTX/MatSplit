using Microsoft.EntityFrameworkCore;

namespace MatSplit.Web.Data;

/// <summary>
/// Brings a database that <c>EnsureCreated()</c> made with an older version up to the current model. MatSplit has no EF
/// migrations yet (see the CHANGELOG), and <c>EnsureCreated()</c> never touches an existing database, so every column that is
/// added later gets an idempotent step here. A fresh database already has it and the step does nothing.
/// </summary>
public static class SchemaUpgrader
{
    public static async Task UpgradeAsync(AppDbContext db, ILogger logger, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(logger);

        // 0.7: the subject of MatIdentity (single sign-on).
        if (!await ColumnExistsAsync(db, "Users", "ExternalSubject", cancellationToken))
        {
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"Users\" ADD COLUMN \"ExternalSubject\" TEXT NULL;", cancellationToken);
            logger.LogInformation("Schema upgrade: added Users.ExternalSubject");
        }

        await db.Database.ExecuteSqlRawAsync(
            "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_Users_ExternalSubject\" ON \"Users\" (\"ExternalSubject\") WHERE \"ExternalSubject\" IS NOT NULL;",
            cancellationToken);
    }

    private static async Task<bool> ColumnExistsAsync(AppDbContext db, string table, string column, CancellationToken cancellationToken)
    {
        var count = await db.Database
            .SqlQuery<int>($"SELECT COUNT(*) AS \"Value\" FROM pragma_table_info({table}) WHERE name = {column}")
            .SingleAsync(cancellationToken);
        return count > 0;
    }
}
