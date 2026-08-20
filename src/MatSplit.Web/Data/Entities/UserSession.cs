namespace MatSplit.Web.Data.Entities;

/// <summary>
/// Server side session record. The auth cookie only carries
/// <see cref="Token"/>, so sessions can be revoked centrally.
/// </summary>
public class UserSession : AuditableEntity
{
    public string Token { get; set; } = Guid.NewGuid().ToString();

    public long UserId { get; set; }

    public DateTime CreatedUtc { get; set; }

    public DateTime ExpiresUtc { get; set; }

    public DateTime LastSeenUtc { get; set; }

    public string? UserAgent { get; set; }

    public User? User { get; set; }
}
