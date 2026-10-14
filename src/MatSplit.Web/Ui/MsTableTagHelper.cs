using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace MatSplit.Web.Ui;

/// <summary>
/// MatUi data table (<c>mat-table-wrap</c> around <c>mat-table</c>). The page supplies thead and tbody,
/// the control supplies the scroll container and the styling; below 720 px the rows become cards. Add
/// data-label="Spaltenname" to every td so the card layout can render the column captions.
/// </summary>
[HtmlTargetElement("ms-table")]
public sealed class MsTableTagHelper : MsTagHelperBase
{
    protected override string ControlName => "table";

    /// <summary>Screen reader caption of the table.</summary>
    public string? Caption { get; set; }

    /// <summary>Renders the caption visible instead of screen reader only.</summary>
    public bool ShowCaption { get; set; }

    /// <summary>Tighter row padding.</summary>
    public bool Dense { get; set; }

    /// <summary>Alternating row background; for long reference tables only.</summary>
    public bool Zebra { get; set; }

    /// <summary>Kept for the pages that name it: the toolkit always turns the rows into cards on a phone.</summary>
    public bool Responsive { get; set; } = true;

    /// <summary>Keeps the header row visible while scrolling.</summary>
    public bool StickyHead { get; set; }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(output);

        var id = ResolveId();
        var children = await output.GetChildContentAsync();

        var table = new TagBuilder("table");
        table.Attributes["id"] = id;
        table.AddCssClass(MsHtml.Classes(
            "mat-table",
            Dense ? "mat-table--compact" : null,
            Zebra ? "mat-table--zebra" : null,
            CssClass));

        if (!string.IsNullOrWhiteSpace(Caption))
        {
            var caption = new TagBuilder("caption");
            caption.AddCssClass(ShowCaption ? "mat-card__sub" : "mat-sr-only");
            caption.InnerHtml.Append(Caption!);
            table.InnerHtml.AppendHtml(caption);
        }

        table.InnerHtml.AppendHtml(children);

        var wrap = new TagBuilder("div");
        wrap.AddCssClass(MsHtml.Classes("mat-table-wrap", StickyHead ? "mat-table-wrap--sticky" : null));
        wrap.Attributes["id"] = id + "-wrap";
        wrap.InnerHtml.AppendHtml(table);

        MsHtml.CopyAttributes(output, table);

        if (context.Items.TryGetValue(typeof(MsListContext), out var raw) && raw is MsListContext list)
        {
            list.Table = wrap;
            output.SuppressOutput();
            return;
        }

        output.TagName = null;
        output.Content.SetHtmlContent(wrap);
    }
}
