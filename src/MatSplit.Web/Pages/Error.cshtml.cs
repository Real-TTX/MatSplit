using System.Diagnostics;
using MatSplit.Web.Ui;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MatSplit.Web.Pages;

/// <summary>
/// Anonymous error page. Program.cs points UseExceptionHandler here in
/// non development environments; the optional ?code= parameter allows other
/// pages to show a plain status code error.
/// </summary>
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
public class ErrorModel(ILogger<ErrorModel> logger) : PageModel
{
    /// <summary>Correlation id, helps to find the entry in the container log.</summary>
    public string? RequestId { get; private set; }

    public bool ShowRequestId => !string.IsNullOrWhiteSpace(RequestId);

    /// <summary>Http status code that is rendered.</summary>
    public int Status { get; private set; } = StatusCodes.Status500InternalServerError;

    public string Headline { get; private set; } = "Etwas ist schiefgelaufen";

    public string Message { get; private set; } = string.Empty;

    /// <summary>Path that produced the error, null when unknown.</summary>
    public string? OriginalPath { get; private set; }

    public void OnGet(int? code)
    {
        RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier;

        var feature = HttpContext.Features.Get<IExceptionHandlerPathFeature>();
        OriginalPath = feature?.Path;

        Status = ResolveStatus(code, feature);

        (Headline, Message) = Status switch
        {
            StatusCodes.Status400BadRequest => (
                "Ung\u00fcltige Anfrage",
                "Die Anfrage konnte nicht verarbeitet werden. Bitte die Eingaben pr\u00fcfen und erneut versuchen."),
            StatusCodes.Status401Unauthorized => (
                "Nicht angemeldet",
                "F\u00fcr diese Seite ist eine Anmeldung n\u00f6tig."),
            StatusCodes.Status403Forbidden => (
                "Kein Zugriff",
                "F\u00fcr diesen Bereich fehlen dir die Rechte."),
            StatusCodes.Status404NotFound => (
                "Seite nicht gefunden",
                "Diese Adresse gibt es nicht (mehr). Vielleicht wurde der Eintrag gel\u00f6scht."),
            _ => (
                "Etwas ist schiefgelaufen",
                "Beim Verarbeiten der Anfrage ist ein Fehler aufgetreten. Der Vorfall wurde im Server-Log vermerkt.")
        };

        if (feature?.Error is not null)
        {
            logger.LogError(
                feature.Error,
                "Unhandled exception on {Path} (request {RequestId})",
                feature.Path,
                RequestId);
        }

        Response.StatusCode = Status;

        this.SetTitle("Fehler", Headline, "warning");
        ViewData[LayoutKeys.HideMenu] = User.Identity?.IsAuthenticated != true;
    }

    private int ResolveStatus(int? code, IExceptionHandlerPathFeature? feature)
    {
        if (code is >= 400 and <= 599)
        {
            return code.Value;
        }

        if (feature is not null)
        {
            return StatusCodes.Status500InternalServerError;
        }

        return Response.StatusCode is >= 400 and <= 599
            ? Response.StatusCode
            : StatusCodes.Status500InternalServerError;
    }
}
