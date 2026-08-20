using System.ComponentModel.DataAnnotations;
using System.Globalization;
using MatSplit.Web.Data.Entities;
using MatSplit.Web.Services;
using MatSplit.Web.Services.Models;
using MatSplit.Web.Ui;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace MatSplit.Web.Pages.Groups.Expenses;

/// <summary>
/// Create and edit page of a single expense (id = 0 or missing creates a new
/// one) including the split configuration and the receipt photos.
/// </summary>
public sealed class EditModel(
    CurrentUserService currentUser,
    GroupService groups,
    ExpenseService expenses,
    AppConfigService appConfig) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public long GroupId { get; set; }

    /// <summary>Id of the edited expense, null or 0 creates a new one.</summary>
    [BindProperty(SupportsGet = true)]
    public long? Id { get; set; }

    [BindProperty]
    public ExpenseInputModel Input { get; set; } = new();

    public Group Group { get; private set; } = new();

    public IReadOnlyList<GroupMember> Members { get; private set; } = [];

    public IReadOnlyList<SelectListItem> MemberOptions { get; private set; } = [];

    public IReadOnlyList<Receipt> Receipts { get; private set; } = [];

    public bool CanManage { get; private set; }

    /// <summary>Groups of the current user, rendered in the left menu.</summary>
    public IReadOnlyList<MenuGroupEntry> MenuGroups { get; private set; } = [];

    public bool IsExisting => Id is > 0;

    public int MaxReceiptSizeMb { get; private set; } = 10;

    /// <summary>Categories already used in this group, shown as a hint.</summary>
    public string? CategoryHint { get; private set; }

    public string ListUrl => $"/Groups/Expenses?groupId={GroupId}";

    public string SelfUrl => IsExisting
        ? $"/Groups/Expenses/Edit?groupId={GroupId}&id={Id}"
        : $"/Groups/Expenses/Edit?groupId={GroupId}";

    public IReadOnlyList<ShareModeOption> ShareModeOptions { get; } =
    [
        new(ExpenseShareModes.Equal, "Gleichmäßig nach Anteilen",
            "Alle Mitglieder zahlen nach ihrem Gruppen-Faktor."),
        new(ExpenseShareModes.Factors, "Individuelle Anteile",
            "Beteiligte auswählen und je Person einen Faktor vergeben."),
        new(ExpenseShareModes.Amounts, "Feste Beträge",
            "Je Person einen festen Betrag erfassen. Die Summe muss dem Gesamtbetrag entsprechen.")
    ];

    /// <summary>Receipt card model, rendered by the _ReceiptPreview partial.</summary>
    public ReceiptPreviewModel ReceiptPreview => new()
    {
        GroupId = GroupId,
        ExpenseId = Id ?? 0,
        Receipts = Receipts,
        CanDeleteAny = CanManage,
        CurrentUserId = currentUser.UserId,
        MaxReceiptSizeMb = MaxReceiptSizeMb,
        UploadUrl = $"{SelfUrl}&handler=UploadReceipt",
        DeleteUrl = $"{SelfUrl}&handler=DeleteReceipt"
    };

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        var guard = await LoadContextAsync(cancellationToken);
        if (guard is not null)
        {
            return guard;
        }

        if (Id is > 0)
        {
            var expense = await expenses.GetExpenseAsync(Id.Value, cancellationToken);
            if (expense is null || expense.GroupId != GroupId)
            {
                return NotFound();
            }

            FillInput(expense);
            Receipts = expense.Receipts.OrderBy(x => x.Id).ToList();
        }
        else
        {
            Id = null;
            FillNewInput();
        }

        ApplyLayout();
        return Page();
    }

    /// <summary>Saves the expense and optionally the receipt photo taken along with it.</summary>
    public async Task<IActionResult> OnPostAsync(IFormFile? receipt, CancellationToken cancellationToken)
    {
        var guard = await LoadContextAsync(cancellationToken);
        if (guard is not null)
        {
            return guard;
        }

        Expense? existing = null;

        if (Id is > 0)
        {
            existing = await expenses.GetExpenseAsync(Id.Value, cancellationToken);
            if (existing is null || existing.GroupId != GroupId)
            {
                return NotFound();
            }

            Receipts = existing.Receipts.OrderBy(x => x.Id).ToList();
        }
        else
        {
            Id = null;
        }

        NormalizeMoneyInputs();
        SyncShareLines();
        ApplyLayout();

        var amountCents = ToCents(Input.Amount ?? 0m);
        var shares = BuildShares(amountCents);

        // Null / 0 is already covered by Required and Range on the property.
        if (Input.PaidByUserId is > 0 && !Members.Any(x => x.UserId == Input.PaidByUserId.Value))
        {
            ModelState.AddModelError("Input.PaidByUserId", "Der Zahler ist kein Mitglied dieser Gruppe.");
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var model = new ExpenseEditModel
        {
            Id = Id ?? 0,
            GroupId = GroupId,
            Description = Input.Description.Trim(),
            AmountCents = amountCents,
            Currency = NormalizeCurrency(Input.Currency),
            PaidByUserId = Input.PaidByUserId ?? 0,
            ExpenseDate = Input.ExpenseDate ?? DateTime.UtcNow.Date,
            Category = string.IsNullOrWhiteSpace(Input.Category) ? null : Input.Category.Trim(),
            Shares = shares
        };

        var result = await expenses.SaveExpenseAsync(model, cancellationToken);

        if (result.IsFailure)
        {
            ModelState.AddModelError(string.Empty, result.Error!);
            return Page();
        }

        var savedId = result.Value.Id;
        var wasInsert = existing is null;
        string? uploadError = null;

        if (receipt is { Length: > 0 })
        {
            uploadError = await UploadAsync(savedId, receipt, cancellationToken);
        }

        if (uploadError is not null)
        {
            this.FlashError($"Die Ausgabe wurde gespeichert, der Beleg jedoch nicht: {uploadError}");
        }
        else if (wasInsert)
        {
            this.Flash("Die Ausgabe wurde angelegt. Jetzt können Belege hinzugefügt werden.");
        }
        else
        {
            this.Flash("Die Ausgabe wurde gespeichert.");
        }

        if (wasInsert)
        {
            return RedirectToPage(new { groupId = GroupId, id = savedId });
        }

        return RedirectToPage("./Index", new { groupId = GroupId });
    }

    /// <summary>Soft deletes the expense, group admins only.</summary>
    public async Task<IActionResult> OnPostDeleteAsync(CancellationToken cancellationToken)
    {
        if (GroupId <= 0 || Id is null or <= 0)
        {
            return NotFound();
        }

        if (!await currentUser.CanManageGroupAsync(GroupId, cancellationToken))
        {
            return Forbid();
        }

        var expense = await expenses.GetExpenseAsync(Id.Value, cancellationToken);
        if (expense is null || expense.GroupId != GroupId)
        {
            return NotFound();
        }

        var result = await expenses.SoftDeleteExpenseAsync(Id.Value, cancellationToken);

        if (result.IsFailure)
        {
            this.FlashError(result.Error!);
            return RedirectToPage(new { groupId = GroupId, id = Id });
        }

        this.Flash("Die Ausgabe wurde gelöscht.");
        return RedirectToPage("./Index", new { groupId = GroupId });
    }

    /// <summary>Multipart upload of a single receipt photo or pdf.</summary>
    public async Task<IActionResult> OnPostUploadReceiptAsync(IFormFile? receipt, CancellationToken cancellationToken)
    {
        if (GroupId <= 0 || Id is null or <= 0)
        {
            return NotFound();
        }

        if (!await currentUser.CanViewGroupAsync(GroupId, cancellationToken))
        {
            return Forbid();
        }

        var expense = await expenses.GetExpenseAsync(Id.Value, cancellationToken);
        if (expense is null || expense.GroupId != GroupId)
        {
            return NotFound();
        }

        if (receipt is null || receipt.Length <= 0)
        {
            this.FlashError("Bitte zuerst eine Datei oder ein Foto auswählen.");
            return RedirectToPage(new { groupId = GroupId, id = Id });
        }

        var error = await UploadAsync(Id.Value, receipt, cancellationToken);

        if (error is not null)
        {
            this.FlashError(error);
            return RedirectToPage(new { groupId = GroupId, id = Id });
        }

        this.Flash("Der Beleg wurde hochgeladen.");
        return RedirectToPage(new { groupId = GroupId, id = Id });
    }

    /// <summary>Removes a receipt. Group admins may delete every receipt, members their own.</summary>
    public async Task<IActionResult> OnPostDeleteReceiptAsync(long receiptId, CancellationToken cancellationToken)
    {
        if (GroupId <= 0 || Id is null or <= 0 || receiptId <= 0)
        {
            return NotFound();
        }

        if (!await currentUser.CanViewGroupAsync(GroupId, cancellationToken))
        {
            return Forbid();
        }

        var expense = await expenses.GetExpenseAsync(Id.Value, cancellationToken);
        if (expense is null || expense.GroupId != GroupId)
        {
            return NotFound();
        }

        var target = expense.Receipts.FirstOrDefault(x => x.Id == receiptId);
        if (target is null)
        {
            return NotFound();
        }

        var canManage = await currentUser.CanManageGroupAsync(GroupId, cancellationToken);
        var isOwner = target.CreateUserId is not null && target.CreateUserId == currentUser.UserId;

        if (!canManage && !isOwner)
        {
            return Forbid();
        }

        var result = await expenses.DeleteReceiptAsync(receiptId, cancellationToken);

        if (result.IsFailure)
        {
            this.FlashError(result.Error!);
            return RedirectToPage(new { groupId = GroupId, id = Id });
        }

        this.Flash("Der Beleg wurde gelöscht.");
        return RedirectToPage(new { groupId = GroupId, id = Id });
    }

    private async Task<IActionResult?> LoadContextAsync(CancellationToken cancellationToken)
    {
        if (GroupId <= 0)
        {
            return NotFound();
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
        Members = await groups.ListMembersAsync(GroupId, cancellationToken);

        MemberOptions = Members
            .Select(m => new SelectListItem(
                MemberName(m),
                m.UserId.ToString(CultureInfo.InvariantCulture)))
            .ToList();

        var config = await appConfig.GetAsync(cancellationToken);
        MaxReceiptSizeMb = config.MaxReceiptSizeMb;

        MenuGroups = await GroupMenu.BuildAsync(groups, currentUser, currentUser.RequireUserId(), cancellationToken);

        var categories = await expenses.ListCategoriesAsync(GroupId, cancellationToken);
        CategoryHint = categories.Count == 0
            ? "Optional, zum Beispiel Essen, Unterkunft oder Fahrt."
            : "Bereits verwendet: " + string.Join(", ", categories.Take(12));

        return null;
    }

    private void ApplyLayout()
    {
        var title = IsExisting ? "Ausgabe bearbeiten" : "Neue Ausgabe";

        this.SetTitle(title, Group.Name, "expense");
        this.SetBreadcrumb(
            new BreadcrumbItem("Gruppen", "/Groups"),
            new BreadcrumbItem(Group.Name, $"/Groups/Details?groupId={GroupId}"),
            new BreadcrumbItem("Ausgaben", ListUrl),
            new BreadcrumbItem(title));
        this.SetMenuGroups(
            MenuGroups.Count == 0 ? [new MenuGroupEntry(Group.Id, Group.Name, CanManage)] : MenuGroups,
            GroupId);
    }

    private void FillNewInput()
    {
        var payerId = Members.Any(x => x.UserId == currentUser.UserId)
            ? currentUser.UserId ?? 0
            : Members.Select(x => x.UserId).FirstOrDefault();

        Input = new ExpenseInputModel
        {
            Id = 0,
            Description = string.Empty,
            Amount = null,
            ExpenseDate = DateTime.UtcNow.Date,
            Currency = NormalizeCurrency(Group.Currency),
            PaidByUserId = payerId,
            ShareMode = ExpenseShareModes.Equal,
            Shares = Members
                .Select(m => new ShareInputModel
                {
                    UserId = m.UserId,
                    DisplayName = MemberName(m),
                    IsIncluded = true,
                    ShareFactor = Math.Max(1, m.ShareFactor)
                })
                .ToList()
        };
    }

    private void FillInput(Expense expense)
    {
        var shares = expense.Shares.ToDictionary(x => x.UserId, x => x);

        Input = new ExpenseInputModel
        {
            Id = expense.Id,
            Description = expense.Description,
            Amount = expense.AmountCents / 100m,
            ExpenseDate = expense.ExpenseDate.Date,
            Currency = NormalizeCurrency(expense.Currency),
            PaidByUserId = expense.PaidByUserId,
            Category = expense.Category,
            ShareMode = DetectShareMode(shares),
            Shares = Members
                .Select(m =>
                {
                    shares.TryGetValue(m.UserId, out var share);

                    return new ShareInputModel
                    {
                        UserId = m.UserId,
                        DisplayName = MemberName(m),
                        IsIncluded = share is not null,
                        ShareFactor = share is null ? Math.Max(1, m.ShareFactor) : Math.Max(1, share.ShareFactor),
                        Amount = share?.ShareAmountCents is { } cents ? cents / 100m : null
                    };
                })
                .ToList()
        };
    }

    /// <summary>
    /// Fixed amounts win, an exact match of all group factors is "equal",
    /// everything else counts as individual factors.
    /// </summary>
    private string DetectShareMode(Dictionary<long, ExpenseShare> shares)
    {
        if (shares.Values.Any(x => x.ShareAmountCents.HasValue))
        {
            return ExpenseShareModes.Amounts;
        }

        if (shares.Count == 0)
        {
            return ExpenseShareModes.Equal;
        }

        if (shares.Count != Members.Count)
        {
            return ExpenseShareModes.Factors;
        }

        foreach (var member in Members)
        {
            if (!shares.TryGetValue(member.UserId, out var share))
            {
                return ExpenseShareModes.Factors;
            }

            if (Math.Max(1, share.ShareFactor) != Math.Max(1, member.ShareFactor))
            {
                return ExpenseShareModes.Factors;
            }
        }

        return ExpenseShareModes.Equal;
    }

    /// <summary>
    /// Re-parses every money field from the raw form value. Asp.net binds form
    /// values with the server culture, while an input[type=number] always posts
    /// a dot as decimal separator - on a German host "30.00" would otherwise
    /// become 3000. Parsing is culture tolerant: the separator that appears last
    /// is the decimal separator.
    /// </summary>
    private void NormalizeMoneyInputs()
    {
        if (!Request.HasFormContentType)
        {
            return;
        }

        var amount = ParseMoney(Request.Form["Input.Amount"]);
        if (amount.HasValue)
        {
            Input.Amount = amount.Value;
            ResetModelStateEntry("Input.Amount");
        }

        for (var index = 0; index < Input.Shares.Count; index++)
        {
            var key = $"Input.Shares[{index}].Amount";
            var parsed = ParseMoney(Request.Form[key]);

            if (!parsed.HasValue)
            {
                continue;
            }

            Input.Shares[index].Amount = parsed.Value;
            ResetModelStateEntry(key);
        }

        // The Range attribute already ran against the value the binder produced,
        // so the check is repeated here on the normalized amount.
        if (ModelState.GetValidationState("Input.Amount") == ModelValidationState.Invalid)
        {
            return;
        }

        if (Input.Amount is null or <= 0m)
        {
            ModelState.AddModelError("Input.Amount", "Bitte einen Betrag größer als 0 angeben.");
        }
        else if (Input.Amount > 1_000_000m)
        {
            ModelState.AddModelError("Input.Amount", "Maximal 1.000.000 pro Ausgabe.");
        }
    }

    /// <summary>Drops binder errors of a field that was re-parsed successfully.</summary>
    private void ResetModelStateEntry(string key)
    {
        if (!ModelState.TryGetValue(key, out var entry))
        {
            return;
        }

        entry.Errors.Clear();
        entry.ValidationState = ModelValidationState.Valid;
    }

    /// <summary>Culture tolerant money parser, null when the text is no number.</summary>
    private static decimal? ParseMoney(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var text = raw.Trim();
        var lastDot = text.LastIndexOf('.');
        var lastComma = text.LastIndexOf(',');

        if (lastDot >= 0 && lastComma >= 0)
        {
            text = lastComma > lastDot
                ? text.Replace(".", string.Empty, StringComparison.Ordinal).Replace(',', '.')
                : text.Replace(",", string.Empty, StringComparison.Ordinal);
        }
        else if (lastComma >= 0)
        {
            text = text.Replace(',', '.');
        }

        return decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    /// <summary>Re-attaches the posted share lines to the current member list.</summary>
    private void SyncShareLines()
    {
        var posted = Input.Shares
            .Where(x => x.UserId > 0)
            .GroupBy(x => x.UserId)
            .ToDictionary(x => x.Key, x => x.First());

        var lines = new List<ShareInputModel>(Members.Count);

        foreach (var member in Members)
        {
            if (posted.TryGetValue(member.UserId, out var line))
            {
                line.UserId = member.UserId;
                line.DisplayName = MemberName(member);
                line.ShareFactor = line.ShareFactor is null or <= 0
                    ? Math.Max(1, member.ShareFactor)
                    : Math.Min(line.ShareFactor.Value, 100);
                lines.Add(line);
                continue;
            }

            lines.Add(new ShareInputModel
            {
                UserId = member.UserId,
                DisplayName = MemberName(member),
                IsIncluded = false,
                ShareFactor = Math.Max(1, member.ShareFactor)
            });
        }

        Input.Shares = lines;

        if (!ExpenseShareModes.IsKnown(Input.ShareMode))
        {
            Input.ShareMode = ExpenseShareModes.Equal;
        }
    }

    /// <summary>
    /// Translates the chosen split mode into the service input. An empty list
    /// tells the service to split across all members by group factor.
    /// </summary>
    private List<ExpenseShareInput> BuildShares(long amountCents)
    {
        if (string.Equals(Input.ShareMode, ExpenseShareModes.Equal, StringComparison.Ordinal))
        {
            return [];
        }

        var isAmountMode = string.Equals(Input.ShareMode, ExpenseShareModes.Amounts, StringComparison.Ordinal);
        var shares = new List<ExpenseShareInput>();
        var sum = 0L;

        for (var index = 0; index < Input.Shares.Count; index++)
        {
            var line = Input.Shares[index];

            if (!line.IsIncluded)
            {
                continue;
            }

            if (!isAmountMode)
            {
                shares.Add(new ExpenseShareInput
                {
                    UserId = line.UserId,
                    ShareFactor = Math.Clamp(line.ShareFactor ?? 1, 1, 100)
                });

                continue;
            }

            var cents = ToCents(line.Amount ?? 0m);

            if (cents <= 0)
            {
                ModelState.AddModelError(
                    $"Input.Shares[{index}].Amount",
                    "Bitte einen Betrag größer als 0 angeben.");
                continue;
            }

            sum += cents;
            shares.Add(new ExpenseShareInput
            {
                UserId = line.UserId,
                ShareFactor = 1,
                ShareAmountCents = cents
            });
        }

        if (shares.Count == 0)
        {
            ModelState.AddModelError("Input.ShareMode", "Bitte mindestens einen Beteiligten auswählen.");
            return shares;
        }

        if (isAmountMode && sum != amountCents)
        {
            ModelState.AddModelError(
                "Input.Amount",
                $"Die festen Beträge ergeben {MsHtml.FormatMoney(sum, Input.Currency)}, der Gesamtbetrag ist {MsHtml.FormatMoney(amountCents, Input.Currency)}.");
        }

        return shares;
    }

    private async Task<string?> UploadAsync(long expenseId, IFormFile file, CancellationToken cancellationToken)
    {
        var config = await appConfig.GetAsync(cancellationToken);
        var maxBytes = (long)config.MaxReceiptSizeMb * 1024 * 1024;

        if (file.Length <= 0)
        {
            return "Die Datei ist leer.";
        }

        if (file.Length > maxBytes)
        {
            return $"Die Datei ist größer als {config.MaxReceiptSizeMb} MB.";
        }

        await using var stream = file.OpenReadStream();
        var result = await expenses.SaveReceiptAsync(expenseId, stream, file.FileName, file.ContentType, cancellationToken);

        return result.IsFailure ? result.Error : null;
    }

    private static string MemberName(GroupMember member)
        => member.User?.DisplayName ?? $"#{member.UserId}";

    private string NormalizeCurrency(string? currency)
    {
        if (!string.IsNullOrWhiteSpace(currency))
        {
            return currency.Trim().ToUpperInvariant();
        }

        return string.IsNullOrWhiteSpace(Group.Currency) ? "EUR" : Group.Currency.ToUpperInvariant();
    }

    private static long ToCents(decimal value)
        => (long)Math.Round(value * 100m, MidpointRounding.AwayFromZero);
}

/// <summary>Keys of the three split modes, also used as radio values.</summary>
public static class ExpenseShareModes
{
    public const string Equal = "Equal";

    public const string Factors = "Factors";

    public const string Amounts = "Amounts";

    public static bool IsKnown(string? value)
        => value is Equal or Factors or Amounts;
}

/// <summary>One radio option of the split selection.</summary>
/// <param name="Value">Posted value.</param>
/// <param name="Label">German caption.</param>
/// <param name="Hint">Explanation below the radio group.</param>
public sealed record ShareModeOption(string Value, string Label, string Hint);

/// <summary>Bound form model of the expense edit page.</summary>
public sealed class ExpenseInputModel
{
    public long Id { get; set; }

    [Required(ErrorMessage = "Bitte eine Beschreibung angeben.")]
    [StringLength(160, ErrorMessage = "Maximal 160 Zeichen.")]
    [Display(Name = "Beschreibung")]
    public string Description { get; set; } = string.Empty;

    [Required(ErrorMessage = "Bitte einen Betrag angeben.")]
    [Range(0.01, 1_000_000, ErrorMessage = "Bitte einen Betrag größer als 0 angeben.")]
    [Display(Name = "Betrag")]
    public decimal? Amount { get; set; }

    [Required(ErrorMessage = "Bitte ein Datum angeben.")]
    [Display(Name = "Datum")]
    [DataType(DataType.Date)]
    public DateTime? ExpenseDate { get; set; }

    [Required(ErrorMessage = "Bitte eine Währung angeben.")]
    [StringLength(3, MinimumLength = 3, ErrorMessage = "Bitte einen dreistelligen Währungscode angeben.")]
    [RegularExpression("[A-Za-z]{3}", ErrorMessage = "Bitte einen dreistelligen Währungscode angeben.")]
    [Display(Name = "Währung")]
    public string Currency { get; set; } = "EUR";

    [StringLength(60, ErrorMessage = "Maximal 60 Zeichen.")]
    [Display(Name = "Kategorie")]
    public string? Category { get; set; }

    [Required(ErrorMessage = "Bitte auswählen, wer bezahlt hat.")]
    [Range(1, long.MaxValue, ErrorMessage = "Bitte auswählen, wer bezahlt hat.")]
    [Display(Name = "Bezahlt von")]
    public long? PaidByUserId { get; set; }

    [Display(Name = "Aufteilung")]
    public string ShareMode { get; set; } = ExpenseShareModes.Equal;

    public List<ShareInputModel> Shares { get; set; } = [];
}

/// <summary>One participant line of the split configuration.</summary>
public sealed class ShareInputModel
{
    public long UserId { get; set; }

    /// <summary>Display only, refilled from the member list on every post.</summary>
    public string DisplayName { get; set; } = string.Empty;

    [Display(Name = "Beteiligt")]
    public bool IsIncluded { get; set; }

    [Required(ErrorMessage = "Bitte einen Faktor angeben.")]
    [Range(1, 100, ErrorMessage = "Der Faktor muss zwischen 1 und 100 liegen.")]
    [Display(Name = "Faktor")]
    public int? ShareFactor { get; set; } = 1;

    [Range(0, 1_000_000, ErrorMessage = "Bitte einen Betrag zwischen 0 und 1.000.000 angeben.")]
    [Display(Name = "Fester Betrag")]
    public decimal? Amount { get; set; }
}

/// <summary>Model of the _ReceiptPreview partial.</summary>
public sealed class ReceiptPreviewModel
{
    public long GroupId { get; init; }

    public long ExpenseId { get; init; }

    public IReadOnlyList<Receipt> Receipts { get; init; } = [];

    /// <summary>True for group admins, they may delete every receipt.</summary>
    public bool CanDeleteAny { get; init; }

    public long? CurrentUserId { get; init; }

    public int MaxReceiptSizeMb { get; init; } = 10;

    public string UploadUrl { get; init; } = string.Empty;

    public string DeleteUrl { get; init; } = string.Empty;

    public bool CanDelete(Receipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);

        return CanDeleteAny
            || (receipt.CreateUserId is not null && receipt.CreateUserId == CurrentUserId);
    }

    public static string FormatSize(long bytes)
    {
        if (bytes >= 1024 * 1024)
        {
            return (bytes / (1024m * 1024m)).ToString("N1", CultureInfo.GetCultureInfo("de-DE")) + " MB";
        }

        if (bytes >= 1024)
        {
            return (bytes / 1024m).ToString("N0", CultureInfo.GetCultureInfo("de-DE")) + " KB";
        }

        return bytes.ToString(CultureInfo.InvariantCulture) + " B";
    }

    public static bool IsImage(Receipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);

        return receipt.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);
    }
}
