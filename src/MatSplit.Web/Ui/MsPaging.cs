using System.Globalization;

namespace MatSplit.Web.Ui;

/// <summary>
/// Helpers around the <c>?page=</c> query parameter.
/// </summary>
/// <remarks>
/// Razor Pages reserves the route value "page" for the page path, so a bound
/// property named "page" receives that path (route beats query in the value
/// provider ranking) and adds a model error that blocks every form on the page.
/// List pages therefore read the page number from the query string by hand and
/// never pass "page" as a route value to <c>RedirectToPage</c>.
/// </remarks>
public static class MsPaging
{
    /// <summary>Query parameter that carries the 1-based page number.</summary>
    public const string PageParameter = "page";

    /// <summary>Reads the 1-based page number from the query string, 1 as fallback.</summary>
    public static int ReadPageNumber(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return int.TryParse(
            request.Query[PageParameter].ToString(),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var value) && value > 0
            ? value
            : 1;
    }
}
