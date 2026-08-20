using System.Globalization;
using System.Reflection;
using MatSplit.Web.Data;
using MatSplit.Web.Infrastructure;
using MatSplit.Web.Services;
using MatSplit.Web.Services.Models;
using MatSplit.Web.Ui;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace MatSplit.Web.Pages.Admin;

/// <summary>
/// Administration dashboard. Shows record counts of the important tables, the
/// size of the data volume and the running version.
/// </summary>
public sealed class IndexModel(
    AppDbContext db,
    MatSplitPaths paths,
    AppConfigService appConfig,
    GroupService groups,
    CurrentUserService currentUser,
    IWebHostEnvironment environment) : PageModel
{
    private static readonly NumberFormatInfo GermanNumbers = new()
    {
        NumberDecimalSeparator = ",",
        NumberGroupSeparator = ".",
        NumberGroupSizes = [3],
        NumberDecimalDigits = 1
    };

    public int UserCount { get; private set; }

    public int AdminCount { get; private set; }

    public int AnonymousCount { get; private set; }

    public int DeletedUserCount { get; private set; }

    public int GroupCount { get; private set; }

    public int DeletedGroupCount { get; private set; }

    public int MembershipCount { get; private set; }

    public int ExpenseCount { get; private set; }

    public long ExpenseTotalCents { get; private set; }

    public int PaymentCount { get; private set; }

    public long PaymentTotalCents { get; private set; }

    public int ActiveSessionCount { get; private set; }

    public int SessionCount { get; private set; }

    public int HistoryCount { get; private set; }

    public int ReceiptCount { get; private set; }

    public string ReceiptSizeText { get; private set; } = "0 B";

    public string DatabaseSizeText { get; private set; } = "0 B";

    public string DatabaseFile => paths.DatabaseFile;

    public string DataRoot => paths.DataRoot;

    public string EnvironmentName => environment.EnvironmentName;

    public string Version { get; private set; } = "0.0.0";

    public AppConfig Config { get; private set; } = new();

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Config = await appConfig.GetAsync(cancellationToken);

        UserCount = await db.Users.CountAsync(x => x.UpdateState != UpdateState.Deleted, cancellationToken);
        AdminCount = await db.Users.CountAsync(
            x => x.UpdateState != UpdateState.Deleted && x.Role == UserRole.Admin, cancellationToken);
        AnonymousCount = await db.Users.CountAsync(
            x => x.UpdateState != UpdateState.Deleted && x.IsAnonymous, cancellationToken);
        DeletedUserCount = await db.Users.CountAsync(x => x.UpdateState == UpdateState.Deleted, cancellationToken);

        GroupCount = await db.Groups.CountAsync(x => x.UpdateState != UpdateState.Deleted, cancellationToken);
        DeletedGroupCount = await db.Groups.CountAsync(x => x.UpdateState == UpdateState.Deleted, cancellationToken);
        MembershipCount = await db.GroupMembers.CountAsync(x => x.UpdateState != UpdateState.Deleted, cancellationToken);

        var expenses = db.Expenses.Where(x => x.UpdateState != UpdateState.Deleted);
        ExpenseCount = await expenses.CountAsync(cancellationToken);
        ExpenseTotalCents = await expenses.SumAsync(x => (long?)x.AmountCents, cancellationToken) ?? 0L;

        var payments = db.Payments.Where(x => x.UpdateState != UpdateState.Deleted);
        PaymentCount = await payments.CountAsync(cancellationToken);
        PaymentTotalCents = await payments.SumAsync(x => (long?)x.AmountCents, cancellationToken) ?? 0L;

        var now = DateTime.UtcNow;
        SessionCount = await db.UserSessions.CountAsync(x => x.UpdateState != UpdateState.Deleted, cancellationToken);
        ActiveSessionCount = await db.UserSessions.CountAsync(
            x => x.UpdateState != UpdateState.Deleted && x.ExpiresUtc > now, cancellationToken);

        HistoryCount = await db.HistoryEntries.CountAsync(cancellationToken);
        ReceiptCount = await db.Receipts.CountAsync(x => x.UpdateState != UpdateState.Deleted, cancellationToken);

        DatabaseSizeText = FormatBytes(MeasureDatabase());
        ReceiptSizeText = FormatBytes(MeasureDirectory(paths.ReceiptsDirectory));
        Version = ResolveVersion();

        var myGroups = await groups.ListGroupsForUserAsync(currentUser.RequireUserId(), cancellationToken: cancellationToken);
        this.SetMenuGroups(myGroups.Select(x => new MenuGroupEntry(x.Id, x.Name)));
        this.SetTitle("Administration", "Systemübersicht und Kennzahlen", "admin");
        this.SetBreadcrumb(new BreadcrumbItem("Administration"));
    }

    /// <summary>Size of the SQLite file including its write ahead log.</summary>
    private long MeasureDatabase()
    {
        var total = 0L;

        foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
        {
            total += FileLength(paths.DatabaseFile + suffix);
        }

        return total;
    }

    private static long FileLength(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists ? info.Length : 0L;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0L;
        }
    }

    private static long MeasureDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            return 0L;
        }

        var total = 0L;

        try
        {
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                total += FileLength(file);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return total;
        }

        return total;
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024L)
        {
            return bytes.ToString("N0", GermanNumbers) + " B";
        }

        var value = bytes / 1024d;

        if (value < 1024d)
        {
            return value.ToString("N1", GermanNumbers) + " KB";
        }

        value /= 1024d;

        if (value < 1024d)
        {
            return value.ToString("N1", GermanNumbers) + " MB";
        }

        value /= 1024d;
        return value.ToString("N1", GermanNumbers) + " GB";
    }

    private static string ResolveVersion()
    {
        var assembly = typeof(IndexModel).Assembly;

        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informational))
        {
            var plus = informational.IndexOf('+', StringComparison.Ordinal);
            return plus > 0 ? informational[..plus] : informational;
        }

        return assembly.GetName().Version?.ToString() ?? "0.0.0";
    }
}
