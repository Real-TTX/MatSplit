using System.ComponentModel.DataAnnotations;
using System.Globalization;
using MatSplit.Web.Data.Entities;
using MatSplit.Web.Services;
using MatSplit.Web.Services.Models;
using MatSplit.Web.Ui;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace MatSplit.Web.Pages.Groups.Payments;

/// <summary>
/// Create (no id) and edit page of a single payment. Amounts are entered as
/// text and converted to cents here, so the value is parsed identically no
/// matter which culture the container runs with.
/// </summary>
public class EditModel(
    CurrentUserService currentUser,
    GroupService groups,
    PaymentService payments) : PageModel
{
    [BindProperty(SupportsGet = true, Name = "groupId")]
    public long GroupId { get; set; }

    [BindProperty(SupportsGet = true, Name = "id")]
    public long? Id { get; set; }

    /// <summary>
    /// Optional prefill for a new payment, used by the settlement suggestions on
    /// /Groups/Balance and /Groups/Details. Amount is invariant long cents.
    /// </summary>
    [BindProperty(SupportsGet = true, Name = "fromUserId")]
    public long? PrefillFromUserId { get; set; }

    /// <inheritdoc cref="PrefillFromUserId" />
    [BindProperty(SupportsGet = true, Name = "toUserId")]
    public long? PrefillToUserId { get; set; }

    /// <inheritdoc cref="PrefillFromUserId" />
    [BindProperty(SupportsGet = true, Name = "amountCents")]
    public long? PrefillAmountCents { get; set; }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public Group Group { get; private set; } = new();

    public List<SelectListItem> MemberOptions { get; } = [];

    /// <summary>True when the current user may soft delete this payment.</summary>
    public bool CanDelete { get; private set; }

    /// <summary>True for group admins, drives the group settings tab.</summary>
    public bool CanManage { get; private set; }

    public bool IsExisting => Input.Id > 0;

    public string Currency => string.IsNullOrWhiteSpace(Group.Currency) ? "EUR" : Group.Currency;

    /// <summary>
    /// Back target: the group hub with the transactions panel open. The hub
    /// replaces the former standalone payment list.
    /// </summary>
    public string ListUrl =>
        "/Groups/Details?groupId=" + GroupId.ToString(CultureInfo.InvariantCulture) + "&tab=transaktionen";

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        var guard = await LoadContextAsync(cancellationToken);
        if (guard is not null)
        {
            return guard;
        }

        if (Id is > 0)
        {
            var payment = await payments.GetPaymentAsync(Id.Value, cancellationToken);

            if (payment is null || payment.GroupId != GroupId)
            {
                return NotFound();
            }

            Input = new InputModel
            {
                Id = payment.Id,
                FromUserId = payment.FromUserId,
                ToUserId = payment.ToUserId,
                Amount = FormatAmount(payment.AmountCents),
                PaymentDate = payment.PaymentDate.Date,
                Note = payment.Note
            };

            CanDelete = await CanDeleteAsync(payment, cancellationToken);
        }
        else
        {
            Input = new InputModel
            {
                FromUserId = PrefillFromUserId ?? currentUser.UserId,
                ToUserId = PrefillToUserId,
                Amount = PrefillAmountCents is > 0 ? FormatAmount(PrefillAmountCents.Value) : string.Empty,
                PaymentDate = DateTime.UtcNow.Date
            };
        }

        await BuildOptionsAsync(cancellationToken);
        ApplyLayout(await MenuGroupsAsync(cancellationToken));
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var guard = await LoadContextAsync(cancellationToken);
        if (guard is not null)
        {
            return guard;
        }

        Payment? existing = null;

        if (Input.Id > 0)
        {
            existing = await payments.GetPaymentAsync(Input.Id, cancellationToken);

            if (existing is null || existing.GroupId != GroupId)
            {
                return NotFound();
            }

            CanDelete = await CanDeleteAsync(existing, cancellationToken);
        }

        // The [Required] rule already reports an empty field, so only a value
        // that is present but unusable gets an extra message.
        var amountCents = 0L;

        if (!string.IsNullOrWhiteSpace(Input.Amount))
        {
            if (!TryParseAmountCents(Input.Amount, out amountCents))
            {
                ModelState.AddModelError("Input.Amount", "Bitte einen Betrag wie 12,50 eingeben.");
            }
            else if (amountCents <= 0)
            {
                ModelState.AddModelError("Input.Amount", "Der Betrag muss größer als 0 sein.");
            }
        }

        if (!ModelState.IsValid)
        {
            return await FailAsync(cancellationToken);
        }

        var model = new PaymentEditModel
        {
            Id = Input.Id,
            GroupId = GroupId,
            FromUserId = Input.FromUserId ?? 0,
            ToUserId = Input.ToUserId ?? 0,
            AmountCents = amountCents,
            PaymentDate = Input.PaymentDate,
            Note = Input.Note
        };

        var result = await payments.SavePaymentAsync(model, cancellationToken);

        if (result.IsFailure)
        {
            ModelState.AddModelError(string.Empty, result.Error!);
            return await FailAsync(cancellationToken);
        }

        this.Flash(existing is null ? "Die Zahlung wurde erfasst." : "Die Zahlung wurde gespeichert.");
        return RedirectToPage("/Groups/Details", new { groupId = GroupId, tab = "transaktionen" });
    }

    public async Task<IActionResult> OnPostDeleteAsync(CancellationToken cancellationToken)
    {
        var guard = await LoadContextAsync(cancellationToken);
        if (guard is not null)
        {
            return guard;
        }

        if (Input.Id <= 0)
        {
            return NotFound();
        }

        var payment = await payments.GetPaymentAsync(Input.Id, cancellationToken);

        if (payment is null || payment.GroupId != GroupId)
        {
            return NotFound();
        }

        if (!await CanDeleteAsync(payment, cancellationToken))
        {
            return Forbid();
        }

        var result = await payments.SoftDeletePaymentAsync(payment.Id, cancellationToken);

        if (result.IsFailure)
        {
            this.FlashError(result.Error!);
            return RedirectToPage("./Edit", new { groupId = GroupId, id = payment.Id });
        }

        this.Flash("Die Zahlung wurde gelöscht.");
        return RedirectToPage("/Groups/Details", new { groupId = GroupId, tab = "transaktionen" });
    }

    /// <summary>Cent value as an invariant decimal for the money input.</summary>
    public static string FormatAmount(long cents)
        => (cents / 100m).ToString("0.00", CultureInfo.InvariantCulture);

    /// <summary>
    /// Accepts "12.50", "12,50" and "1.234,56". Html number inputs always post
    /// the invariant format, hand typed values may use the German comma, so both
    /// separators are supported instead of relying on the request culture.
    /// </summary>
    public static bool TryParseAmountCents(string? text, out long cents)
    {
        cents = 0;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var value = text.Trim()
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("€", string.Empty, StringComparison.Ordinal);

        var lastComma = value.LastIndexOf(',');
        var lastDot = value.LastIndexOf('.');

        if (lastComma >= 0 && lastDot >= 0)
        {
            // Both separators present: the rightmost one is the decimal point.
            value = lastComma > lastDot
                ? value.Replace(".", string.Empty, StringComparison.Ordinal).Replace(",", ".", StringComparison.Ordinal)
                : value.Replace(",", string.Empty, StringComparison.Ordinal);
        }
        else if (lastComma >= 0)
        {
            value = value.Replace(",", ".", StringComparison.Ordinal);
        }

        if (!decimal.TryParse(
                value,
                NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture,
                out var amount))
        {
            return false;
        }

        if (Math.Abs(amount) > 100_000_000m)
        {
            return false;
        }

        cents = (long)Math.Round(amount * 100m, MidpointRounding.AwayFromZero);
        return true;
    }

    private async Task<IActionResult?> LoadContextAsync(CancellationToken cancellationToken)
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
        return null;
    }

    private async Task<IActionResult> FailAsync(CancellationToken cancellationToken)
    {
        await BuildOptionsAsync(cancellationToken);
        ApplyLayout(await MenuGroupsAsync(cancellationToken));
        return Page();
    }

    /// <summary>
    /// Group admins may remove any payment, everybody else only the payments
    /// they are involved in.
    /// </summary>
    private async Task<bool> CanDeleteAsync(Payment payment, CancellationToken cancellationToken)
    {
        if (await currentUser.CanManageGroupAsync(GroupId, cancellationToken))
        {
            return true;
        }

        var userId = currentUser.UserId;
        return userId is not null && (payment.FromUserId == userId.Value || payment.ToUserId == userId.Value);
    }

    private async Task BuildOptionsAsync(CancellationToken cancellationToken)
    {
        var members = await groups.ListMembersAsync(GroupId, cancellationToken);

        foreach (var member in members)
        {
            MemberOptions.Add(new SelectListItem
            {
                Value = member.UserId.ToString(CultureInfo.InvariantCulture),
                Text = member.User?.DisplayName ?? "Benutzer " + member.UserId.ToString(CultureInfo.InvariantCulture)
            });
        }
    }

    private async Task<IReadOnlyList<MenuGroupEntry>> MenuGroupsAsync(CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();
        return await GroupMenu.BuildAsync(groups, currentUser, userId, cancellationToken);
    }

    private void ApplyLayout(IReadOnlyList<MenuGroupEntry> menuGroups)
    {
        var title = IsExisting ? "Zahlung bearbeiten" : "Neue Zahlung";

        this.SetTitle(title, Group.Name, "paypal");
        this.SetBreadcrumb(
            new BreadcrumbItem(Group.Name, "/Groups/Details?groupId=" + GroupId.ToString(CultureInfo.InvariantCulture)),
            new BreadcrumbItem("Zahlungen", ListUrl),
            new BreadcrumbItem(title));

        this.SetMenuGroups(menuGroups, GroupId);
    }

    /// <summary>Form model of the payment editor.</summary>
    public sealed class InputModel
    {
        public long Id { get; set; }

        [Required(ErrorMessage = "Bitte den Zahler auswählen.")]
        [Display(Name = "Von (zahlt)")]
        public long? FromUserId { get; set; }

        [Required(ErrorMessage = "Bitte den Empfänger auswählen.")]
        [Display(Name = "An (erhält)")]
        public long? ToUserId { get; set; }

        [Required(ErrorMessage = "Bitte einen Betrag angeben.")]
        [Display(Name = "Betrag")]
        public string Amount { get; set; } = string.Empty;

        [Required(ErrorMessage = "Bitte ein Datum angeben.")]
        [DataType(DataType.Date)]
        [Display(Name = "Datum")]
        public DateTime PaymentDate { get; set; } = DateTime.UtcNow.Date;

        [StringLength(500, ErrorMessage = "Maximal 500 Zeichen.")]
        [Display(Name = "Notiz")]
        public string? Note { get; set; }
    }
}
