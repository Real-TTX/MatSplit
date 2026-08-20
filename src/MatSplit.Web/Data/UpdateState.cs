namespace MatSplit.Web.Data;

/// <summary>
/// Soft-delete / lifecycle marker stored on every auditable record.
/// </summary>
public enum UpdateState
{
    Deleted = 0,
    Created = 1,
    Updated = 2
}
