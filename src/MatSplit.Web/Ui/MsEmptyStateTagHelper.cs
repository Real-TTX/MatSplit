using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace MatSplit.Web.Ui;

/// <summary>
/// Placeholder shown instead of an empty table. Inside a ms-list it is rendered
/// between table and pagination.
/// </summary>
[HtmlTargetElement("ms-empty-state")]
public sealed class MsEmptyStateTagHelper : MsTagHelperBase
{
    protected override string ControlName => "empty";

    /// <summary>Headline of the placeholder.</summary>
    public string Title { get; set; } = "Noch keine Eintr\u00e4ge";

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
        wrap.AddCssClass(MsHtml.Classes("ms-empty", CssClass));

        var iconWrap = new TagBuilder("div");
        iconWrap.AddCssClass("ms-empty__icon");
        iconWrap.InnerHtml.AppendHtml(MsHtml.Icon(Icon, 40));
        wrap.InnerHtml.AppendHtml(iconWrap);

        var title = new TagBuilder("p");
        title.AddCssClass("ms-empty__title");
        title.InnerHtml.Append(Title);
        wrap.InnerHtml.AppendHtml(title);

        if (!string.IsNullOrWhiteSpace(Text))
        {
            var text = new TagBuilder("p");
            text.AddCssClass("ms-empty__text");
            text.InnerHtml.Append(Text!);
            wrap.InnerHtml.AppendHtml(text);
        }

        if (!string.IsNullOrWhiteSpace(ActionUrl) || MsHtml.HasContent(children))
        {
            var actions = new TagBuilder("div");
            actions.AddCssClass("ms-empty__actions");

            if (!string.IsNullOrWhiteSpace(ActionUrl))
            {
                var link = new TagBuilder("a");
                link.AddCssClass("ms-btn ms-btn--primary");
                link.Attributes["href"] = ActionUrl!;
                link.Attributes["id"] = id + "-action";
                link.InnerHtml.AppendHtml(MsHtml.Icon(ActionIcon, 18));

                var label = new TagBuilder("span");
                label.AddCssClass("ms-btn__label");
                label.InnerHtml.Append(ActionLabel ?? "Neu anlegen");
                link.InnerHtml.AppendHtml(label);
                actions.InnerHtml.AppendHtml(link);
            }

            if (MsHtml.HasContent(children))
            {
                actions.InnerHtml.AppendHtml(children);
            }

            wrap.InnerHtml.AppendHtml(actions);
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
