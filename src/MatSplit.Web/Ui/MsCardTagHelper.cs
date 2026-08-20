using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace MatSplit.Web.Ui;

/// <summary>
/// Surface with optional heading. Used for detail blocks, forms and dashboard
/// tiles.
/// </summary>
[HtmlTargetElement("ms-card")]
public sealed class MsCardTagHelper : MsTagHelperBase
{
    protected override string ControlName => "card";

    /// <summary>Card heading.</summary>
    public string? Title { get; set; }

    /// <summary>Text below the heading.</summary>
    public string? Subtitle { get; set; }

    /// <summary>Icon name from the sprite.</summary>
    public string? Icon { get; set; }

    /// <summary>default, accent, success, warning or danger.</summary>
    public string? Tone { get; set; }

    /// <summary>Makes the whole card a link.</summary>
    public string? Href { get; set; }

    /// <summary>Removes the inner padding, e.g. when the card contains a table.</summary>
    public bool Flush { get; set; }

    /// <summary>Heading level of the title, 2 by default.</summary>
    public int Level { get; set; } = 2;

    /// <summary>
    /// Optional quick action in the card header, e.g. a plus to add an item
    /// directly. Renders a compact icon link on the right of the heading.
    /// </summary>
    public string? HeaderActionUrl { get; set; }

    /// <summary>Icon name for the header action, plus by default.</summary>
    public string? HeaderActionIcon { get; set; } = "plus";

    /// <summary>Accessible label and tooltip for the header action.</summary>
    public string? HeaderActionLabel { get; set; }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(output);

        var id = ResolveId();
        var children = await output.GetChildContentAsync();
        var isLink = !string.IsNullOrWhiteSpace(Href);

        output.TagName = isLink ? "a" : "section";
        output.TagMode = TagMode.StartTagAndEndTag;
        output.Attributes.SetAttribute("id", id);
        MsHtml.AddClass(output, MsHtml.Classes(
            "ms-card",
            string.IsNullOrWhiteSpace(Tone) ? null : "ms-card--" + Tone!.Trim().ToLowerInvariant(),
            Flush ? "ms-card--flush" : null,
            isLink ? "ms-card--link" : null,
            CssClass));

        if (isLink)
        {
            output.Attributes.SetAttribute("href", Href!);
        }

        var hasTitle = !string.IsNullOrWhiteSpace(Title);
        var hasHeaderAction = !string.IsNullOrWhiteSpace(HeaderActionUrl);

        if (hasTitle || hasHeaderAction)
        {
            var head = new TagBuilder("header");
            head.AddCssClass("ms-card__head");

            var headText = new TagBuilder("div");
            headText.AddCssClass("ms-card__headtext");

            if (hasTitle)
            {
                var level = Level is >= 1 and <= 6 ? Level : 2;
                var heading = new TagBuilder("h" + level.ToString(System.Globalization.CultureInfo.InvariantCulture));
                heading.AddCssClass("ms-card__title");
                heading.Attributes["id"] = id + "-title";

                if (!string.IsNullOrWhiteSpace(Icon))
                {
                    heading.InnerHtml.AppendHtml(MsHtml.Icon(Icon, 20));
                }

                var text = new TagBuilder("span");
                text.InnerHtml.Append(Title!);
                heading.InnerHtml.AppendHtml(text);
                headText.InnerHtml.AppendHtml(heading);

                if (!string.IsNullOrWhiteSpace(Subtitle))
                {
                    var subtitle = new TagBuilder("p");
                    subtitle.AddCssClass("ms-card__subtitle");
                    subtitle.InnerHtml.Append(Subtitle!);
                    headText.InnerHtml.AppendHtml(subtitle);
                }
            }

            head.InnerHtml.AppendHtml(headText);

            if (hasHeaderAction)
            {
                var label = string.IsNullOrWhiteSpace(HeaderActionLabel) ? "Hinzufügen" : HeaderActionLabel!.Trim();
                var iconName = string.IsNullOrWhiteSpace(HeaderActionIcon) ? "plus" : HeaderActionIcon!.Trim();

                var action = new TagBuilder("a");
                action.AddCssClass("ms-card__action");
                action.Attributes["href"] = HeaderActionUrl!;
                action.Attributes["aria-label"] = label;
                action.Attributes["title"] = label;
                action.InnerHtml.AppendHtml(MsHtml.Icon(iconName, 20));
                head.InnerHtml.AppendHtml(action);
            }

            output.PreContent.AppendHtml(head);
            if (hasTitle)
            {
                output.Attributes.SetAttribute("aria-labelledby", id + "-title");
            }
        }

        var body = new TagBuilder("div");
        body.AddCssClass("ms-card__body");
        body.InnerHtml.AppendHtml(children);
        output.Content.SetHtmlContent(body);
    }
}
