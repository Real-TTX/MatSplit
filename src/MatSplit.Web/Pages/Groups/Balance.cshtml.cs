using System.Globalization;
using MatSplit.Web.Data.Entities;
using MatSplit.Web.Infrastructure;
using MatSplit.Web.Services;
using MatSplit.Web.Services.Models;
using MatSplit.Web.Ui;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MatSplit.Web.Pages.Groups;

/// <summary>
/// Balance of a group: what every member paid, what their share is and the
/// minimal set of transfers that settles the group. Every suggested transfer can
/// be booked as a payment with one click.
/// </summary>
public class BalanceModel(
    CurrentUserService currentUser,
    GroupService groups,
    BalanceService balances,
    PaymentService payments,
    HistoryService history) : PageModel
{
    [BindProperty(SupportsGet = true, Name = "groupId")]
    public long GroupId { get; set; }

    public Group Group { get; private set; } = new();

    public BalanceResult Result { get; private set; } = new();

    /// <summary>PayPal address per member, used for the hint next to a transfer.</summary>
    public Dictionary<long, string?> PayPalAddresses { get; } = [];

    /// <summary>True for group admins, drives the group settings tab.</summary>
    public bool CanManage { get; private set; }

    public long? CurrentUserId => currentUser.UserId;

    public string Currency => string.IsNullOrWhiteSpace(Result.Currency) ? "EUR" : Result.Currency;

    /// <summary>Sum of all open settlement transfers.</summary>
    public long OpenSettlementCents => Result.Settlements.Sum(x => x.AmountCents);

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        var guard = await LoadAsync(cancellationToken);
        return guard ?? Page();
    }

    /// <summary>Books one suggested transfer as a real payment.</summary>
    public async Task<IActionResult> OnPostSettleAsync(
        long fromUserId,
        long toUserId,
        long amountCents,
        CancellationToken cancellationToken)
    {
        if (GroupId <= 0)
        {
            return RedirectToPage("/Groups/Index");
        }

        if (!await currentUser.CanViewGroupAsync(GroupId, cancellationToken))
        {
            return Forbid();
        }

        var group = await groups.GetGroupAsync(GroupId, cancellationToken);
        if (group is null)
        {
            return NotFound();
        }

        if (fromUserId <= 0 || toUserId <= 0 || fromUserId == toUserId || amountCents <= 0)
        {
            this.FlashError("Die Ausgleichszahlung konnte nicht verbucht werden.");
            return RedirectToPage(new { groupId = GroupId });
        }

        var result = await payments.SavePaymentAsync(
            new PaymentEditModel
            {
                Id = 0,
                GroupId = GroupId,
                FromUserId = fromUserId,
                ToUserId = toUserId,
                AmountCents = amountCents,
                PaymentDate = DateTime.UtcNow.Date,
                Note = "Ausgleichszahlung"
            },
            cancellationToken);

        if (result.IsFailure)
        {
            this.FlashError(result.Error!);
            return RedirectToPage(new { groupId = GroupId });
        }

        var members = await groups.ListMembersAsync(GroupId, cancellationToken);
        var fromName = NameOf(members, fromUserId);
        var toName = NameOf(members, toUserId);
        var amount = (amountCents / 100m).ToString("0.00", CultureInfo.InvariantCulture);

        await history.LogAsync(
            GroupId,
            currentUser.UserId,
            HistoryService.EntityTypes.Payment,
            result.Value.Id,
            HistoryService.Actions.Settled,
            $"Ausgleichszahlung von {fromName} an {toName} über {amount} {group.Currency} verbucht.",
            cancellationToken: cancellationToken);

        this.Flash($"Ausgleichszahlung von {fromName} an {toName} wurde verbucht.");
        return RedirectToPage(new { groupId = GroupId });
    }

    /// <summary>Share of all expenses without the payments received.</summary>
    public static long ShareCents(MemberBalance balance)
    {
        ArgumentNullException.ThrowIfNull(balance);
        return balance.OwedCents - balance.PaymentsReceivedCents;
    }

    /// <summary>Payments sent minus payments received.</summary>
    public static long NetPaymentsCents(MemberBalance balance)
    {
        ArgumentNullException.ThrowIfNull(balance);
        return balance.PaymentsSentCents - balance.PaymentsReceivedCents;
    }

    /// <summary>True when the balance belongs to the signed in user.</summary>
    public bool IsCurrentUser(long userId) => CurrentUserId == userId;

    /// <summary>
    /// Hint shown when no paypal.me link could be built for the receiver.
    /// </summary>
    public string PayPalHint(Settlement settlement)
    {
        ArgumentNullException.ThrowIfNull(settlement);

        PayPalAddresses.TryGetValue(settlement.ToUserId, out var address);

        if (string.IsNullOrWhiteSpace(address))
        {
            return IsCurrentUser(settlement.ToUserId)
                ? "Du hast noch keine PayPal-Adresse hinterlegt."
                : $"{settlement.ToDisplayName} hat keine PayPal-Adresse hinterlegt.";
        }

        return PayPalLinkBuilder.ExtractHandle(address) is null
            ? "Die hinterlegte PayPal-Adresse ist kein paypal.me-Handle."
            : "Kein PayPal-Link verfügbar.";
    }

    private static string NameOf(IReadOnlyList<GroupMember> members, long userId)
    {
        var member = members.FirstOrDefault(x => x.UserId == userId);
        return member?.User?.DisplayName ?? "Benutzer " + userId.ToString(CultureInfo.InvariantCulture);
    }

    private async Task<IActionResult?> LoadAsync(CancellationToken cancellationToken)
    {
        if (GroupId <= 0)
        {
            return RedirectToPage("/Groups/Index");
        }

        if (!await currentUser.CanViewGroupAsync(GroupId, cancellationToken))
        {
            return Forbid();
        }

        var group = await groups.GetGroupAsync(GroupId, cancellationToken);
        if (group is null)
        {
            return NotFound();
        }

        Group = group;
        CanManage = await currentUser.CanManageGroupAsync(GroupId, cancellationToken);
        Result = await balances.CalculateBalancesAsync(GroupId, cancellationToken);

        var members = await groups.ListMembersAsync(GroupId, cancellationToken);

        foreach (var member in members)
        {
            PayPalAddresses[member.UserId] = member.User?.PayPalAddress;
        }

        var menu = await GroupMenu.BuildAsync(
            groups,
            currentUser,
            currentUser.RequireUserId(),
            cancellationToken);

        this.SetTitle("Kontostand", Group.Name, "balance");
        this.SetBreadcrumb(
            new BreadcrumbItem(Group.Name, "/Groups/Details?groupId=" + GroupId.ToString(CultureInfo.InvariantCulture)),
            new BreadcrumbItem("Kontostand"));
        this.SetMenuGroups(menu, GroupId);

        return null;
    }
}
