namespace MatSplit.Web.Data.Entities;

/// <summary>
/// Append-only audit trail shown on the group history page.
/// </summary>
public class HistoryEntry : AuditableEntity
{
    public long? GroupId { get; set; }

    public long? UserId { get; set; }

    public string EntityType { get; set; } = string.Empty;

    public long? EntityId { get; set; }

    public string Action { get; set; } = string.Empty;

    public string Summary { get; set; } = string.Empty;

    public string? DetailsJson { get; set; }

    public Group? Group { get; set; }

    public User? User { get; set; }
}
