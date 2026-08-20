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

        if (!string.IsNullOrWhiteSpace(Title))
        {
            var head = new TagBuilder("header");
            head.AddCssClass("ms-card__head");

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
            head.InnerHtml.AppendHtml(heading);

            if (!string.IsNullOrWhiteSpace(Subtitle))
            {
                var subtitle = new TagBuilder("p");
                subtitle.AddCssClass("ms-card__subtitle");
                subtitle.InnerHtml.Append(Subtitle!);
                head.InnerHtml.AppendHtml(subtitle);
            }

            output.PreContent.AppendHtml(head);
            output.Attributes.SetAttribute("aria-labelledby", id + "-title");
        }

        var body = new TagBuilder("div");
        body.AddCssClass("ms-card__body");
        body.InnerHtml.AppendHtml(children);
        output.Content.SetHtmlContent(body);
    }
}
