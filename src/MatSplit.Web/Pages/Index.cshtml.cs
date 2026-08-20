using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MatSplit.Web.Pages;

/// <summary>
/// Dashboard entry point. The group list is the real home screen of MatSplit,
/// so "/" simply forwards there (see specification: Index = redirect to groups).
/// </summary>
public class IndexModel : PageModel
{
    public IActionResult OnGet() => RedirectToPage("/Groups/Index");
}
