namespace MatSplit.Web.Services.Models;

/// <summary>
/// Whether a <see cref="TransactionRow"/> originates from an expense or a
/// payment. Used for the type badge and the type filter of the combined
/// transaction list on the group hub.
/// </summary>
public enum TransactionKind
{
    /// <summary>An expense paid by one member on behalf of the group.</summary>
    Expense,

    /// <summary>Money that actually moved between two members.</summary>
    Payment
}

/// <summary>
/// A single row of the combined, chronological transaction list shown on the
/// group hub (/Groups/Details). This is a pure presentation union: the amounts
/// and data still live in the <c>Expenses</c> respectively <c>Payments</c>
/// tables, only the display is merged. Every amount is a signed-free
/// <see cref="AmountCents"/> value in cents, rendered via <c>ms-money</c>.
/// </summary>
public sealed class TransactionRow
{
    /// <summary>Expense or payment.</summary>
    public required TransactionKind Kind { get; init; }

    /// <summary>Primary label of the row (expense description / payment note).</summary>
    public required string Title { get; init; }

    /// <summary>
    /// Secondary line: "bezahlt von &lt;Name&gt;" for an expense,
    /// "&lt;Von&gt; → &lt;An&gt;" for a payment.
    /// </summary>
    public required string Subtitle { get; init; }

    /// <summary>Amount in cents, always positive.</summary>
    public required long AmountCents { get; init; }

    /// <summary>Date the expense / payment happened on (local, date only).</summary>
    public required DateTime Date { get; init; }

    /// <summary>Target of the row: the matching edit sub page.</summary>
    public required string EditUrl { get; init; }

    /// <summary>Sprite icon name for the row circle.</summary>
    public required string Icon { get; init; }

    /// <summary>German type label for the badge ("Ausgabe" / "Zahlung").</summary>
    public required string TypeLabel { get; init; }
}
