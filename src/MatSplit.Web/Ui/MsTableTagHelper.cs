using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace MatSplit.Web.Ui;

/// <summary>
/// Data table. The page supplies thead and tbody, the control supplies the
/// scroll container, the styling and the mobile card layout. Add
/// data-label="Spaltenname" to every td so the mobile card layout can render
/// the column captions.
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

    /// <summary>Alternating row background.</summary>
    public bool Zebra { get; set; } = true;

    /// <summary>Turns the rows into cards on small screens.</summary>
    public bool Responsive { get; set; } = true;

    /// <summary>Keeps the header row visible while scrolling.</summary>
    public bool StickyHead { get; set; } = true;

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(output);

        var id = ResolveId();
        var children = await output.GetChildContentAsync();

        var table = new TagBuilder("table");
        table.Attributes["id"] = id;
        table.AddCssClass(MsHtml.Classes(
            "ms-table",
            Dense ? "ms-table--dense" : null,
            Zebra ? "ms-table--zebra" : null,
            Responsive ? "ms-table--cards" : null,
            StickyHead ? "ms-table--sticky" : null,
            CssClass));

        if (!string.IsNullOrWhiteSpace(Caption))
        {
            var caption = new TagBuilder("caption");
            caption.AddCssClass(ShowCaption ? "ms-table__caption" : "ms-visually-hidden");
            caption.InnerHtml.Append(Caption!);
            table.InnerHtml.AppendHtml(caption);
        }

        table.InnerHtml.AppendHtml(children);

        var wrap = new TagBuilder("div");
        wrap.AddCssClass("ms-table-wrap");
        wrap.Attributes["id"] = id + "-wrap";
        wrap.Attributes["data-ms-scroll"] = "true";
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
