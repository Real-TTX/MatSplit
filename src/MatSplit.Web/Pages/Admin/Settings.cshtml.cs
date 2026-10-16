using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using MatSplit.Web.Infrastructure;
using MatSplit.Web.Services;
using MatSplit.Web.Services.Models;
using MatSplit.Web.Ui;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MatSplit.Web.Pages.Admin;

/// <summary>
/// Editor for /data/config/appconfig.json (application name, default currency,
/// invite links, session lifetime and the receipt size limit).
/// </summary>
public sealed class SettingsModel(
    AppConfigService appConfig,
    MatSplitPaths paths,
    GroupService groups,
    HistoryService history,
    CurrentUserService currentUser) : PageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new();

    public string ConfigFile => paths.ConfigFile;

    public string DataRoot => paths.DataRoot;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var config = await appConfig.GetAsync(cancellationToken);

        Input = new InputModel
        {
            AppName = config.AppName,
            DefaultCurrency = config.DefaultCurrency,
            AllowAnonymousJoin = config.AllowAnonymousJoin,
            SessionLifetimeDays = config.SessionLifetimeDays,
            MaxReceiptSizeMb = config.MaxReceiptSizeMb,
            CompressReceipts = config.CompressReceipts,
            ReceiptTargetKb = config.ReceiptTargetKb
        };

        await PrepareLayoutAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var config = new AppConfig
        {
            AppName = Input.AppName,
            DefaultCurrency = Input.DefaultCurrency,
            AllowAnonymousJoin = Input.AllowAnonymousJoin,
            SessionLifetimeDays = Input.SessionLifetimeDays,
            MaxReceiptSizeMb = Input.MaxReceiptSizeMb,
            CompressReceipts = Input.CompressReceipts,
            ReceiptTargetKb = Input.ReceiptTargetKb
        };

        // The same rules as PUT /api/configure; the attributes below drive the check in the browser.
        if (ModelState.IsValid)
        {
            foreach (var (property, error) in AppConfigRules.Validate(config))
            {
                ModelState.AddModelError($"{nameof(Input)}.{property}", error);
            }
        }

        if (!ModelState.IsValid)
        {
            await PrepareLayoutAsync(cancellationToken);
            return Page();
        }

        var previous = await appConfig.GetAsync(cancellationToken);

        var result = await appConfig.SaveAsync(config, cancellationToken);

        if (result.IsFailure)
        {
            ModelState.AddModelError(string.Empty, result.Error!);
            await PrepareLayoutAsync(cancellationToken);
            return Page();
        }

        var details = JsonSerializer.Serialize(new
        {
            Before = previous,
            After = appConfig.Current
        });

        await history.LogAsync(
            null,
            currentUser.UserId,
            HistoryService.EntityTypes.AppConfig,
            null,
            HistoryService.Actions.Updated,
            "Anwendungseinstellungen wurden geändert.",
            details,
            cancellationToken: cancellationToken);

        this.Flash("Die Einstellungen wurden gespeichert.");
        return RedirectToPage();
    }

    private async Task PrepareLayoutAsync(CancellationToken cancellationToken)
    {
        var myGroups = await groups.ListGroupsForUserAsync(currentUser.RequireUserId(), cancellationToken: cancellationToken);
        this.SetMenuGroups(myGroups.Select(x => new MenuGroupEntry(x.Id, x.Name)));
        this.SetTitle("Einstellungen", "Globale Konfiguration dieser Installation", "settings");
        this.SetBreadcrumb(
            new BreadcrumbItem("Administration", "/Admin"),
            new BreadcrumbItem("Einstellungen"));
    }

    /// <summary>Form values of the settings editor.</summary>
    public sealed class InputModel
    {
        [Required(ErrorMessage = AppConfigRules.AppNameRequired)]
        [StringLength(AppConfigRules.AppNameMaxLength, ErrorMessage = AppConfigRules.AppNameTooLong)]
        [Display(Name = "Anwendungsname")]
        public string AppName { get; set; } = "MatSplit";

        [Required(ErrorMessage = AppConfigRules.CurrencyRequired)]
        [RegularExpression(AppConfigRules.CurrencyPattern, ErrorMessage = AppConfigRules.CurrencyInvalid)]
        [Display(Name = "Standardwährung")]
        public string DefaultCurrency { get; set; } = "EUR";

        [Display(Name = "Beitritt über Einladungslinks erlauben")]
        public bool AllowAnonymousJoin { get; set; } = true;

        [Required(ErrorMessage = "Bitte die Sitzungsdauer in Tagen angeben.")]
        [Range(AppConfigRules.MinSessionLifetimeDays, AppConfigRules.MaxSessionLifetimeDays, ErrorMessage = AppConfigRules.SessionLifetimeRange)]
        [Display(Name = "Sitzungsdauer in Tagen")]
        public int SessionLifetimeDays { get; set; } = 30;

        [Required(ErrorMessage = "Bitte die maximale Beleggröße angeben.")]
        [Range(AppConfigRules.MinReceiptSizeMb, AppConfigRules.MaxReceiptSizeMb, ErrorMessage = AppConfigRules.ReceiptSizeRange)]
        [Display(Name = "Maximale Beleggröße in MB")]
        public int MaxReceiptSizeMb { get; set; } = 10;

        [Display(Name = "Belegfotos im Browser verkleinern")]
        public bool CompressReceipts { get; set; } = true;

        [Required(ErrorMessage = "Bitte die Ziel-Beleggröße angeben.")]
        [Range(AppConfigRules.MinReceiptTargetKb, AppConfigRules.MaxReceiptTargetKb, ErrorMessage = AppConfigRules.ReceiptTargetRange)]
        [Display(Name = "Ziel-Beleggröße in KB")]
        public int ReceiptTargetKb { get; set; } = 500;
    }
}
