using MatSplit.Web.Data;
using MatSplit.Web.Services.Models;
using Microsoft.EntityFrameworkCore;

namespace MatSplit.Web.Services;

/// <summary>
/// Figures and checks of the whole installation for the platform API (tiles and <c>/api/health</c>), so the platform code
/// never touches the <see cref="AppDbContext"/> itself.
/// </summary>
public sealed class InstanceStatusService(AppDbContext db, BalanceService balances, ILogger<InstanceStatusService> logger)
{
    /// <summary>The counts behind the tiles. Open settlements are the suggested transfers of every active group.</summary>
    public async Task<InstanceSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var activeUsers = db.Users.AsNoTracking()
            .Where(x => x.UpdateState != UpdateState.Deleted && x.MergedIntoUserId == null);

        var people = await activeUsers.CountAsync(x => !x.IsAnonymous, cancellationToken);
        var guests = await activeUsers.CountAsync(x => x.IsAnonymous, cancellationToken);

        var groupIds = await db.Groups.AsNoTracking()
            .Where(x => x.UpdateState != UpdateState.Deleted)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

        var since = DateTime.UtcNow.Date.AddDays(-30);
        var expenses = db.Expenses.AsNoTracking()
            .Where(x => x.UpdateState != UpdateState.Deleted && x.Group != null && x.Group.UpdateState != UpdateState.Deleted);
        var recentExpenses = await expenses.CountAsync(x => x.ExpenseDate >= since, cancellationToken);

        var openSettlements = 0;
        foreach (var groupId in groupIds)
        {
            var balance = await balances.CalculateBalancesAsync(groupId, cancellationToken);
            openSettlements += balance.Settlements.Count;
        }

        var receiptBytes = await db.Receipts.AsNoTracking()
            .Where(x => x.UpdateState != UpdateState.Deleted)
            .SumAsync(x => (long?)x.FileSizeBytes, cancellationToken) ?? 0L;

        var lastExpense = await expenses.MaxAsync(x => (DateTime?)x.CreateDate, cancellationToken);
        var lastPayment = await db.Payments.AsNoTracking()
            .Where(x => x.UpdateState != UpdateState.Deleted)
            .MaxAsync(x => (DateTime?)x.CreateDate, cancellationToken);
        var lastActivity = lastExpense > lastPayment || lastPayment is null ? lastExpense : lastPayment;

        return new InstanceSnapshot(groupIds.Count, people, guests, recentExpenses, openSettlements, receiptBytes, lastActivity);
    }

    /// <summary>True when the database answers a simple query.</summary>
    public async Task<bool> CanReadDatabaseAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _ = await db.Users.AsNoTracking().AnyAsync(cancellationToken);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Health check: the database cannot be read");
            return false;
        }
    }

    /// <summary>Active administrators (not deleted, not merged).</summary>
    public async Task<int> CountActiveAdminsAsync(CancellationToken cancellationToken = default)
        => await db.Users.AsNoTracking().CountAsync(
            x => x.Role == UserRole.Admin && x.UpdateState != UpdateState.Deleted && x.MergedIntoUserId == null,
            cancellationToken);
}
