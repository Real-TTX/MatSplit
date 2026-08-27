namespace MatSplit.Web.Data.Entities;

/// <summary>
/// A shared expense group (holiday trip, flat share, ...).
/// </summary>
public class Group : AuditableEntity
{
    public string Token { get; set; } = Guid.NewGuid().ToString();

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string Currency { get; set; } = "EUR";

    /// <summary>Secret used by the public join page (/Join?token=...).</summary>
    public string InviteToken { get; set; } = Guid.NewGuid().ToString();

    public bool InviteEnabled { get; set; } = true;

    /// <summary>Secret used by the public, read-only view page (/View?token=...).</summary>
    public string ReadOnlyToken { get; set; } = Guid.NewGuid().ToString();

    /// <summary>When off, the read-only link does not resolve. Off by default.</summary>
    public bool ReadOnlyEnabled { get; set; }

    public ICollection<GroupMember> Members { get; set; } = new List<GroupMember>();

    public ICollection<Expense> Expenses { get; set; } = new List<Expense>();

    public ICollection<Payment> Payments { get; set; } = new List<Payment>();
}
