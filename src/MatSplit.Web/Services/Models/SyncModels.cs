namespace MatSplit.Web.Services.Models;

/// <summary>
/// One offline captured expense pushed by the service worker to
/// <c>POST /api/sync/expenses</c>.
/// </summary>
public sealed class SyncExpenseDto
{
    /// <summary>Client side id used to correlate the response, may be null.</summary>
    public string? ClientId { get; set; }

    public long GroupId { get; set; }

    public string Description { get; set; } = string.Empty;

    public long AmountCents { get; set; }

    public string? Currency { get; set; }

    /// <summary>Payer. 0 falls back to the signed in user.</summary>
    public long PaidByUserId { get; set; }

    public DateTime? ExpenseDate { get; set; }

    public string? Category { get; set; }

    public List<SyncExpenseShareDto> Shares { get; set; } = [];
}

/// <summary>Participant line of a synced expense.</summary>
public sealed class SyncExpenseShareDto
{
    public long UserId { get; set; }

    public int ShareFactor { get; set; } = 1;

    public long? ShareAmountCents { get; set; }
}

/// <summary>Per item outcome of a sync push.</summary>
public sealed class SyncExpenseResultDto
{
    public string? ClientId { get; set; }

    public long ExpenseId { get; set; }

    public bool Success { get; set; }

    public string? Error { get; set; }
}

/// <summary>Envelope returned by <c>POST /api/sync/expenses</c>.</summary>
public sealed class SyncExpenseResponseDto
{
    public int Accepted { get; set; }

    public int Rejected { get; set; }

    public List<SyncExpenseResultDto> Results { get; set; } = [];
}
