using MatSplit.Web.Data;
using MatSplit.Web.Data.Entities;
using MatSplit.Web.Infrastructure;
using MatSplit.Web.Services.Models;
using Microsoft.EntityFrameworkCore;

namespace MatSplit.Web.Services;

/// <summary>
/// Append-only activity log per group. Every mutating service call writes one
/// entry so /Groups/History can show who changed what.
/// </summary>
public sealed class HistoryService(AppDbContext db, IHttpContextAccessor httpContextAccessor)
{
    /// <summary>Well known values for the Action column.</summary>
    public static class Actions
    {
        public const string Created = "Created";
        public const string Updated = "Updated";
        public const string Deleted = "Deleted";
        public const string Joined = "Joined";
        public const string Left = "Left";
        public const string Merged = "Merged";
        public const string Uploaded = "Uploaded";
        public const string Settled = "Settled";
        public const string SignedIn = "SignedIn";
        public const string SignedOut = "SignedOut";
    }

    /// <summary>Well known values for the EntityType column.</summary>
    public static class EntityTypes
    {
        public const string Group = "Group";
        public const string GroupMember = "GroupMember";
        public const string Expense = "Expense";
        public const string Payment = "Payment";
        public const string Receipt = "Receipt";
        public const string User = "User";
        public const string AppConfig = "AppConfig";
    }

    /// <summary>
    /// Writes one history entry. Does <b>not</b> call SaveChanges when
    /// <paramref name="saveChanges"/> is false, so callers can batch it into
    /// their own transaction.
    /// </summary>
    public async Task<HistoryEntry> LogAsync(
        long? groupId,
        long? userId,
        string entityType,
        long? entityId,
        string action,
        string summary,
        string? detailsJson = null,
        bool saveChanges = true,
        CancellationToken cancellationToken = default)
    {
        var entry = new HistoryEntry
        {
            GroupId = groupId,
            // Callers pass null when the acting user is simply "whoever is
            // signed in". Only background work (no HttpContext) stays null and
            // shows up as "System" in the list.
            UserId = userId ?? MatSplitClaims.GetUserId(httpContextAccessor.HttpContext?.User),
            EntityType = entityType,
            EntityId = entityId,
            Action = action,
            Summary = Truncate(summary, 1000),
            DetailsJson = detailsJson,
            UpdateState = UpdateState.Created
        };

        db.HistoryEntries.Add(entry);

        if (saveChanges)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return entry;
    }

    /// <summary>
    /// Paged history of a group (or of the whole system when
    /// <paramref name="groupId"/> is null). Sort keys: date_desc (default),
    /// date, action, action_desc, user, user_desc.
    /// </summary>
    public async Task<PagedResult<HistoryEntry>> ListHistoryAsync(
        long? groupId,
        string? search = null,
        string? action = null,
        int page = 1,
        int pageSize = Paging.DefaultPageSize,
        DateTime? fromUtc = null,
        DateTime? toUtc = null,
        string? sort = null,
        CancellationToken cancellationToken = default)
    {
        var (safePage, safeSize) = Paging.Normalize(page, pageSize);

        var query = db.HistoryEntries
            .AsNoTracking()
            .Include(x => x.User)
            .Where(x => x.UpdateState != UpdateState.Deleted);

        // CreateDate is stored as utc; callers that filter by a local day have to
        // convert the day bounds themselves.
        if (fromUtc.HasValue)
        {
            var from = fromUtc.Value;
            query = query.Where(x => x.CreateDate >= from);
        }

        if (toUtc.HasValue)
        {
            var to = toUtc.Value;
            query = query.Where(x => x.CreateDate <= to);
        }

        if (groupId.HasValue)
        {
            query = query.Where(x => x.GroupId == groupId.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x =>
                EF.Functions.Like(x.Summary, "%" + term + "%")
                || EF.Functions.Like(x.EntityType, "%" + term + "%")
                || (x.User != null && EF.Functions.Like(x.User.DisplayName, "%" + term + "%")));
        }

        if (!string.IsNullOrWhiteSpace(action))
        {
            var actionFilter = action.Trim();
            query = query.Where(x => x.Action == actionFilter);
        }

        var total = await query.CountAsync(cancellationToken);

        query = sort switch
        {
            "date" => query.OrderBy(x => x.CreateDate).ThenBy(x => x.Id),
            "action" => query.OrderBy(x => x.Action).ThenByDescending(x => x.CreateDate),
            "action_desc" => query.OrderByDescending(x => x.Action).ThenByDescending(x => x.CreateDate),
            "user" => query.OrderBy(x => x.User!.DisplayName).ThenByDescending(x => x.CreateDate),
            "user_desc" => query.OrderByDescending(x => x.User!.DisplayName).ThenByDescending(x => x.CreateDate),
            _ => query.OrderByDescending(x => x.CreateDate).ThenByDescending(x => x.Id)
        };

        var items = await query
            .Skip(Paging.Skip(safePage, safeSize))
            .Take(safeSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<HistoryEntry>(items, safePage, safeSize, total);
    }

    /// <summary>Distinct actions present in a group, for the filter dropdown.</summary>
    public async Task<IReadOnlyList<string>> ListActionsAsync(long? groupId, CancellationToken cancellationToken = default)
    {
        var query = db.HistoryEntries.AsNoTracking().Where(x => x.UpdateState != UpdateState.Deleted);

        if (groupId.HasValue)
        {
            query = query.Where(x => x.GroupId == groupId.Value);
        }

        return await query
            .Select(x => x.Action)
            .Distinct()
            .OrderBy(x => x)
            .ToListAsync(cancellationToken);
    }

    private static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value.Length <= maxLength ? value : value[..maxLength];
    }
}
