namespace MatSplit.Web.Data.Entities;

/// <summary>
/// Participation of a user in an expense. When
/// <see cref="ShareAmountCents"/> is null the amount is derived from
/// <see cref="ShareFactor"/> proportionally.
/// </summary>
public class ExpenseShare : AuditableEntity
{
    public long ExpenseId { get; set; }

    public long UserId { get; set; }

    public int ShareFactor { get; set; } = 1;

    /// <summary>Fixed amount; null means "split by factor".</summary>
    public long? ShareAmountCents { get; set; }

    public Expense? Expense { get; set; }

    public User? User { get; set; }
}
