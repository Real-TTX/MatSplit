using System.Globalization;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace MatSplit.Web.Ui;

/// <summary>
/// MatUi pager (<c>mat-pager</c>), rendered directly below the table. Renders nothing when the
/// result fits on a single page. The url template either contains the
/// placeholder {0} for the page number or the page parameter is appended to the
/// given url.
/// </summary>
[HtmlTargetElement("ms-pagination")]
public sealed class MsPaginationTagHelper : MsTagHelperBase
{
    private const string Placeholder = "{0}";

    protected override string ControlName => "pagination";

    /// <summary>Current page, 1 based.</summary>
    public int Page { get; set; } = 1;

    /// <summary>Records per page.</summary>
    public int PageSize { get; set; } = 20;

    /// <summary>Total number of records.</summary>
    public int TotalCount { get; set; }

    /// <summary>Url template, either containing {0} or used as base url.</summary>
    public string? PageUrl { get; set; }

    /// <summary>Query parameter name used when the template has no placeholder.</summary>
    public string PageParam { get; set; } = "page";

    /// <summary>Maximum number of numbered links around the current page.</summary>
    public int Window { get; set; } = 2;

    /// <summary>Renders the "51 bis 75 von 190" hint inside the navigation.</summary>
    public bool ShowInfo { get; set; } = true;

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(output);

        var pageSize = PageSize <= 0 ? 20 : PageSize;
        var totalPages = TotalCount <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)pageSize);

        if (totalPages <= 1)
        {
            output.SuppressOutput();
            return;
        }

        var current = Math.Clamp(Page <= 0 ? 1 : Page, 1, totalPages);
        var id = ResolveId();

        var nav = new TagBuilder("nav");
        nav.Attributes["id"] = id;
        nav.Attributes["aria-label"] = "Seitennavigation";
        nav.AddCssClass(MsHtml.Classes("mat-pager", CssClass));

        if (ShowInfo)
        {
            var first = ((current - 1) * pageSize) + 1;
            var last = Math.Min(current * pageSize, TotalCount);
            var info = new TagBuilder("span");
            info.AddCssClass("mat-pager__info");
            info.InnerHtml.Append(string.Format(CultureInfo.InvariantCulture, "{0} bis {1} von {2}", first, last, TotalCount));
            nav.InnerHtml.AppendHtml(info);
        }

        var pages = new TagBuilder("div");
        pages.AddCssClass("mat-pager__pages");

        pages.InnerHtml.AppendHtml(Step(id + "-prev", current - 1, current > 1, "chevron-left", "Vorherige Seite"));

        var lastRendered = 0;

        for (var number = 1; number <= totalPages; number++)
        {
            if (!IsVisible(number, current, totalPages))
            {
                continue;
            }

            if (lastRendered > 0 && number - lastRendered > 1)
            {
                var gap = new TagBuilder("span");
                gap.AddCssClass("mat-pager__gap");
                gap.Attributes["aria-hidden"] = "true";
                gap.InnerHtml.Append("…");
                pages.InnerHtml.AppendHtml(gap);
            }

            pages.InnerHtml.AppendHtml(NumberLink(id, number, number == current));
            lastRendered = number;
        }

        pages.InnerHtml.AppendHtml(Step(id + "-next", current + 1, current < totalPages, "chevron-right", "Nächste Seite"));
        nav.InnerHtml.AppendHtml(pages);

        MsHtml.CopyAttributes(output, nav);

        if (context.Items.TryGetValue(typeof(MsListContext), out var raw) && raw is MsListContext list)
        {
            list.Pagination = nav;
            output.SuppressOutput();
            return;
        }

        output.TagName = null;
        output.Content.SetHtmlContent(nav);
    }

    private bool IsVisible(int number, int current, int totalPages)
    {
        if (number == 1 || number == totalPages)
        {
            return true;
        }

        var window = Window < 1 ? 1 : Window;
        return Math.Abs(number - current) <= window;
    }

    private TagBuilder NumberLink(string id, int number, bool active)
    {
        var text = number.ToString(CultureInfo.InvariantCulture);

        if (active)
        {
            var marker = new TagBuilder("span");
            marker.AddCssClass("mat-pager__btn is-current");
            marker.Attributes["aria-current"] = "page";
            marker.Attributes["id"] = id + "-page-" + text;
            marker.InnerHtml.Append(text);
            return marker;
        }

        var link = new TagBuilder("a");
        link.AddCssClass("mat-pager__btn");
        link.Attributes["href"] = BuildUrl(number);
        link.Attributes["id"] = id + "-page-" + text;
        link.Attributes["aria-label"] = "Seite " + text;
        link.InnerHtml.Append(text);
        return link;
    }

    private TagBuilder Step(string id, int target, bool enabled, string icon, string label)
    {
        if (!enabled)
        {
            var disabled = new TagBuilder("span");
            disabled.AddCssClass("mat-pager__btn is-disabled");
            disabled.Attributes["aria-hidden"] = "true";
            disabled.Attributes["id"] = id;
            disabled.InnerHtml.AppendHtml(MsHtml.Icon(icon));
            return disabled;
        }

        var link = new TagBuilder("a");
        link.AddCssClass("mat-pager__btn");
        link.Attributes["href"] = BuildUrl(target);
        link.Attributes["id"] = id;
        link.Attributes["aria-label"] = label;
        link.Attributes["rel"] = string.Equals(icon, "chevron-left", StringComparison.Ordinal) ? "prev" : "next";
        link.InnerHtml.AppendHtml(MsHtml.Icon(icon));
        return link;
    }

    private string BuildUrl(int number)
    {
        var text = number.ToString(CultureInfo.InvariantCulture);
        var template = PageUrl;

        if (string.IsNullOrWhiteSpace(template))
        {
            template = ViewContext.HttpContext.Request.Path.Value ?? "/";
        }

        if (template.Contains(Placeholder, StringComparison.Ordinal))
        {
            return template.Replace(Placeholder, text, StringComparison.Ordinal);
        }

        var separator = template.Contains('?', StringComparison.Ordinal) ? "&" : "?";

        if (template.EndsWith('?') || template.EndsWith('&'))
        {
            separator = string.Empty;
        }

        return template + separator + PageParam + "=" + text;
    }
}
