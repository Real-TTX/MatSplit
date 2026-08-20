namespace MatSplit.Web.Services.Models;

/// <summary>
/// Complete balance snapshot of a group as returned by
/// <see cref="BalanceService.CalculateBalancesAsync"/>.
/// </summary>
public sealed class BalanceResult
{
    public long GroupId { get; init; }

    public string Currency { get; init; } = "EUR";

    /// <summary>Sum of all (non deleted) expenses of the group.</summary>
    public long TotalExpensesCents { get; init; }

    /// <summary>Sum of all recorded payments between members.</summary>
    public long TotalPaymentsCents { get; init; }

    public IReadOnlyList<MemberBalance> Balances { get; init; } = [];

    /// <summary>Minimal set of transfers that settles the group.</summary>
    public IReadOnlyList<Settlement> Settlements { get; init; } = [];
}

/// <summary>
/// Per-member view of the group balance. Positive
/// <see cref="BalanceCents"/> means the member gets money back.
/// </summary>
public sealed class MemberBalance
{
    public long UserId { get; init; }

    public string DisplayName { get; init; } = string.Empty;

    public int ShareFactor { get; init; }

    /// <summary>What the member paid for the group (expenses + payments sent).</summary>
    public long PaidCents { get; init; }

    /// <summary>The member's share of all expenses (+ payments received).</summary>
    public long OwedCents { get; init; }

    /// <summary>PaidCents - OwedCents. Positive = credit, negative = debt.</summary>
    public long BalanceCents { get; init; }

    /// <summary>Expenses actually paid out of this member's pocket.</summary>
    public long ExpensesPaidCents { get; init; }

    /// <summary>Money this member handed to other members.</summary>
    public long PaymentsSentCents { get; init; }

    /// <summary>Money this member received from other members.</summary>
    public long PaymentsReceivedCents { get; init; }

    public bool IsCreditor => BalanceCents > 0;

    public bool IsDebtor => BalanceCents < 0;
}

/// <summary>
/// A single suggested transfer to settle the group.
/// </summary>
public sealed class Settlement
{
    public long FromUserId { get; init; }

    public string FromDisplayName { get; init; } = string.Empty;

    public long ToUserId { get; init; }

    public string ToDisplayName { get; init; } = string.Empty;

    public long AmountCents { get; init; }

    /// <summary>paypal.me link of the receiver, null when not available.</summary>
    public string? PayPalUrl { get; init; }
}
