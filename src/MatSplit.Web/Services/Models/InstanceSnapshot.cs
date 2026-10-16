namespace MatSplit.Web.Services.Models;

/// <summary>The figures of the whole installation that the platform API shows as tiles (<c>GET /api/state</c>).</summary>
public sealed record InstanceSnapshot(
    int Groups,
    int People,
    int Guests,
    int ExpensesLast30Days,
    int OpenSettlements,
    long ReceiptBytes,
    DateTime? LastActivityUtc);
