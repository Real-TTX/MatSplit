namespace MatSplit.Web.Services.Models;

/// <summary>
/// Input model for <see cref="ExpenseService.SaveExpenseAsync"/>.
/// <see cref="Id"/> = 0 inserts, everything else updates.
/// </summary>
public sealed class ExpenseEditModel
{
    public long Id { get; set; }

    public long GroupId { get; set; }

    public string Description { get; set; } = string.Empty;

    public long AmountCents { get; set; }

    public string Currency { get; set; } = "EUR";

    public long PaidByUserId { get; set; }

    public DateTime ExpenseDate { get; set; } = DateTime.UtcNow.Date;

    public string? Category { get; set; }

    /// <summary>
    /// Participants of the expense. An empty list means "split across all
    /// current group members using their group share factor".
    /// </summary>
    public List<ExpenseShareInput> Shares { get; set; } = [];
}

/// <summary>
/// One participant line of an expense.
/// </summary>
public sealed class ExpenseShareInput
{
    public long UserId { get; set; }

    public int ShareFactor { get; set; } = 1;

    /// <summary>Fixed amount in cents; null splits by <see cref="ShareFactor"/>.</summary>
    public long? ShareAmountCents { get; set; }
}
