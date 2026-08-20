namespace MatSplit.Web.Data.Entities;

/// <summary>
/// Membership of a <see cref="Entities.User"/> in a <see cref="Entities.Group"/>
/// including the default share factor (e.g. a family counts as 3).
/// </summary>
public class GroupMember : AuditableEntity
{
    public long GroupId { get; set; }

    public long UserId { get; set; }

    public int ShareFactor { get; set; } = 1;

    public bool IsGroupAdmin { get; set; }

    public Group? Group { get; set; }

    public User? User { get; set; }
}
