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
            MaxReceiptSizeMb = config.MaxReceiptSizeMb
        };

        await PrepareLayoutAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            await PrepareLayoutAsync(cancellationToken);
            return Page();
        }

        var previous = await appConfig.GetAsync(cancellationToken);

        var config = new AppConfig
        {
            AppName = Input.AppName,
            DefaultCurrency = Input.DefaultCurrency,
            AllowAnonymousJoin = Input.AllowAnonymousJoin,
            SessionLifetimeDays = Input.SessionLifetimeDays,
            MaxReceiptSizeMb = Input.MaxReceiptSizeMb
        };

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
        [Required(ErrorMessage = "Bitte einen Namen angeben.")]
        [StringLength(60, ErrorMessage = "Maximal 60 Zeichen.")]
        [Display(Name = "Anwendungsname")]
        public string AppName { get; set; } = "MatSplit";

        [Required(ErrorMessage = "Bitte eine Währung angeben.")]
        [RegularExpression("^[A-Za-z]{3}$", ErrorMessage = "Bitte einen dreistelligen Währungscode angeben, z. B. EUR.")]
        [Display(Name = "Standardwährung")]
        public string DefaultCurrency { get; set; } = "EUR";

        [Display(Name = "Beitritt über Einladungslinks erlauben")]
        public bool AllowAnonymousJoin { get; set; } = true;

        [Required(ErrorMessage = "Bitte die Sitzungsdauer in Tagen angeben.")]
        [Range(1, 365, ErrorMessage = "Bitte einen Wert zwischen 1 und 365 Tagen angeben.")]
        [Display(Name = "Sitzungsdauer in Tagen")]
        public int SessionLifetimeDays { get; set; } = 30;

        [Required(ErrorMessage = "Bitte die maximale Beleggröße angeben.")]
        [Range(1, 100, ErrorMessage = "Bitte einen Wert zwischen 1 und 100 MB angeben.")]
        [Display(Name = "Maximale Beleggröße in MB")]
        public int MaxReceiptSizeMb { get; set; } = 10;
    }
}
