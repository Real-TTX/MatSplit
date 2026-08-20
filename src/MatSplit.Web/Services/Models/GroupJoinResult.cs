using MatSplit.Web.Data.Entities;

namespace MatSplit.Web.Services.Models;

/// <summary>
/// Outcome of <see cref="GroupService.JoinByInviteTokenAsync"/>. The Join page
/// uses <see cref="User"/> to sign the freshly created anonymous member in.
/// </summary>
public sealed class GroupJoinResult
{
    public required Group Group { get; init; }

    public required User User { get; init; }

    /// <summary>False when the user was already a member of the group.</summary>
    public bool CreatedNewMembership { get; init; }
}

/// <summary>
/// Receipt plus its absolute location on disk, returned by
/// <see cref="ExpenseService.GetReceiptPathAsync"/>.
/// </summary>
public sealed class ReceiptFile
{
    public required Receipt Receipt { get; init; }

    /// <summary>Absolute path inside the receipts directory.</summary>
    public required string AbsolutePath { get; init; }

    public bool Exists => File.Exists(AbsolutePath);
}
