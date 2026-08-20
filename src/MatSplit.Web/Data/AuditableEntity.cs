namespace MatSplit.Web.Data;

/// <summary>
/// Base class for every persisted entity. Provides the surrogate key and the
/// audit columns that <see cref="Infrastructure.AuditInterceptor"/> maintains.
/// </summary>
public abstract class AuditableEntity
{
    public long Id { get; set; }

    public DateTime CreateDate { get; set; }

    public long? CreateUserId { get; set; }

    public DateTime UpdateDate { get; set; }

    public long? UpdateUserId { get; set; }

    public UpdateState UpdateState { get; set; } = UpdateState.Created;

    /// <summary>True when the record has been soft deleted.</summary>
    public bool IsDeleted => UpdateState == UpdateState.Deleted;
}
