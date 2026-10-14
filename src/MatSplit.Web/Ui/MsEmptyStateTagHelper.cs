using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace MatSplit.Web.Ui;

/// <summary>
/// MatUi empty state (<c>mat-empty</c>): shown instead of an empty table, says why and offers the way
/// out. Inside a ms-list it is rendered between table and pagination.
/// </summary>
[HtmlTargetElement("ms-empty-state")]
public sealed class MsEmptyStateTagHelper : MsTagHelperBase
{
    protected override string ControlName => "empty";

    /// <summary>Headline of the placeholder.</summary>
    public string Title { get; set; } = "Noch keine Einträge";

    /// <summary>Explanation below the headline.</summary>
    public string? Text { get; set; }

    /// <summary>Icon name from the sprite.</summary>
    public string Icon { get; set; } = "empty";

    /// <summary>Target of the call to action button.</summary>
    [HtmlAttributeName("action-url")]
    public string? ActionUrl { get; set; }

    /// <summary>Caption of the call to action button.</summary>
    [HtmlAttributeName("action-label")]
    public string? ActionLabel { get; set; }

    /// <summary>Icon of the call to action button.</summary>
    [HtmlAttributeName("action-icon")]
    public string ActionIcon { get; set; } = "plus";

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(output);

        var id = ResolveId();
        var children = await output.GetChildContentAsync();

        var wrap = new TagBuilder("div");
        wrap.Attributes["id"] = id;
        wrap.AddCssClass(MsHtml.Classes("mat-empty", CssClass));

        var iconWrap = new TagBuilder("span");
        iconWrap.AddCssClass("mat-empty__icon");
        iconWrap.InnerHtml.AppendHtml(MsHtml.Icon(Icon));
        wrap.InnerHtml.AppendHtml(iconWrap);

        var title = new TagBuilder("div");
        title.AddCssClass("mat-empty__title");
        title.InnerHtml.Append(Title);
        wrap.InnerHtml.AppendHtml(title);

        if (!string.IsNullOrWhiteSpace(Text))
        {
            var text = new TagBuilder("p");
            text.InnerHtml.Append(Text!);
            wrap.InnerHtml.AppendHtml(text);
        }

        if (!string.IsNullOrWhiteSpace(ActionUrl))
        {
            var link = new TagBuilder("a");
            link.AddCssClass("mat-btn mat-btn--primary");
            link.Attributes["href"] = ActionUrl!;
            link.Attributes["id"] = id + "-action";
            link.InnerHtml.AppendHtml(MsHtml.Icon(ActionIcon));

            var label = new TagBuilder("span");
            label.InnerHtml.Append(ActionLabel ?? "Neu anlegen");
            link.InnerHtml.AppendHtml(label);
            wrap.InnerHtml.AppendHtml(link);
        }

        if (MsHtml.HasContent(children))
        {
            wrap.InnerHtml.AppendHtml(children);
        }

        MsHtml.CopyAttributes(output, wrap);

        if (context.Items.TryGetValue(typeof(MsListContext), out var raw) && raw is MsListContext list)
        {
            list.EmptyState = wrap;
            output.SuppressOutput();
            return;
        }

        output.TagName = null;
        output.Content.SetHtmlContent(wrap);
    }
}
