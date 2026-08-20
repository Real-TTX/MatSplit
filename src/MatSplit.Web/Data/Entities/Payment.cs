namespace MatSplit.Web.Data.Entities;

/// <summary>
/// Money that actually changed hands between two group members.
/// </summary>
public class Payment : AuditableEntity
{
    public long GroupId { get; set; }

    public long FromUserId { get; set; }

    public long ToUserId { get; set; }

    public long AmountCents { get; set; }

    public DateTime PaymentDate { get; set; }

    public string? Note { get; set; }

    public Group? Group { get; set; }

    public User? FromUser { get; set; }

    public User? ToUser { get; set; }
}
