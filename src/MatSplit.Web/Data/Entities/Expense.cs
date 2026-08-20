namespace MatSplit.Web.Data.Entities;

/// <summary>
/// A single expense paid by one group member on behalf of the group.
/// </summary>
public class Expense : AuditableEntity
{
    public long GroupId { get; set; }

    public string Description { get; set; } = string.Empty;

    public long AmountCents { get; set; }

    public string Currency { get; set; } = "EUR";

    public long PaidByUserId { get; set; }

    public DateTime ExpenseDate { get; set; }

    public string? Category { get; set; }

    public Group? Group { get; set; }

    public User? PaidByUser { get; set; }

    public ICollection<ExpenseShare> Shares { get; set; } = new List<ExpenseShare>();

    public ICollection<Receipt> Receipts { get; set; } = new List<Receipt>();
}
