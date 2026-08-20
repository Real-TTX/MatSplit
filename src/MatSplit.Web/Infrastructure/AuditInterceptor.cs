using MatSplit.Web.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace MatSplit.Web.Infrastructure;

/// <summary>
/// Fills the audit columns of every <see cref="AuditableEntity"/> before it is
/// written. Services therefore never have to touch CreateDate / UpdateDate /
/// CreateUserId / UpdateUserId manually.
/// </summary>
public sealed class AuditInterceptor(IHttpContextAccessor httpContextAccessor) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        ApplyAudit(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ApplyAudit(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void ApplyAudit(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var now = DateTime.UtcNow;
        var userId = MatSplitClaims.GetUserId(httpContextAccessor.HttpContext?.User);

        foreach (var entry in context.ChangeTracker.Entries<AuditableEntity>())
        {
            if (entry.State == EntityState.Added)
            {
                if (entry.Entity.CreateDate == default)
                {
                    entry.Entity.CreateDate = now;
                }

                entry.Entity.CreateUserId ??= userId;
                entry.Entity.UpdateDate = now;
                entry.Entity.UpdateUserId = userId;

                if (entry.Entity.UpdateState == default)
                {
                    entry.Entity.UpdateState = UpdateState.Created;
                }

                continue;
            }

            if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdateDate = now;
                entry.Entity.UpdateUserId = userId;

                // Never let an update silently overwrite the create audit trail.
                entry.Property(x => x.CreateDate).IsModified = false;
                entry.Property(x => x.CreateUserId).IsModified = false;
            }
        }
    }
}
