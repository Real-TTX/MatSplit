using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace MatSplit.Web.Ui;

/// <summary>
/// Wrapper for every list page. Collects the slots ms-toolbar, ms-table,
/// ms-pagination and ms-actions and renders them in the order required by the
/// ui guideline: toolbar above, table, pagination directly below the table and
/// the list actions at the bottom.
/// </summary>
[HtmlTargetElement("ms-list")]
public sealed class MsListTagHelper : MsTagHelperBase
{
    protected override string ControlName => "list";

    /// <summary>Optional headline of the list.</summary>
    public string? Title { get; set; }

    /// <summary>Optional text below the headline.</summary>
    public string? Subtitle { get; set; }

    /// <summary>Icon name shown next to the headline.</summary>
    public string? Icon { get; set; }

    /// <summary>Total number of records, rendered as badge next to the headline.</summary>
    public int? Count { get; set; }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(output);

        var id = ResolveId();
        var slots = new MsListContext(id);
        context.Items[typeof(MsListContext)] = slots;

        var freeContent = await output.GetChildContentAsync();

        output.TagName = "section";
        output.TagMode = TagMode.StartTagAndEndTag;
        output.Attributes.SetAttribute("id", id);
        MsHtml.AddClass(output, MsHtml.Classes("ms-list", CssClass));

        var body = new HtmlContentBuilder();

        if (!string.IsNullOrWhiteSpace(Title))
        {
            body.AppendHtml(BuildHead(id));
        }

        if (slots.Toolbar is not null)
        {
            body.AppendHtml(slots.Toolbar);
        }

        if (!freeContent.IsEmptyOrWhiteSpace)
        {
            var free = new TagBuilder("div");
            free.AddCssClass("ms-list__free");
            free.InnerHtml.AppendHtml(freeContent);
            body.AppendHtml(free);
        }

        if (slots.Table is not null)
        {
            body.AppendHtml(slots.Table);
        }

        if (slots.EmptyState is not null)
        {
            body.AppendHtml(slots.EmptyState);
        }

        if (slots.Pagination is not null)
        {
            body.AppendHtml(slots.Pagination);
        }

        if (slots.Actions is not null)
        {
            body.AppendHtml(slots.Actions);
        }

        output.Content.SetHtmlContent(body);
    }

    private IHtmlContent BuildHead(string id)
    {
        var head = new TagBuilder("header");
        head.AddCssClass("ms-list__head");

        var heading = new TagBuilder("h2");
        heading.AddCssClass("ms-list__title");
        heading.Attributes["id"] = id + "-title";

        if (!string.IsNullOrWhiteSpace(Icon))
        {
            heading.InnerHtml.AppendHtml(MsHtml.Icon(Icon, 20));
        }

        var titleText = new TagBuilder("span");
        titleText.InnerHtml.Append(Title ?? string.Empty);
        heading.InnerHtml.AppendHtml(titleText);

        if (Count.HasValue)
        {
            var badge = new TagBuilder("span");
            badge.AddCssClass("ms-badge");
            badge.InnerHtml.Append(Count.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
            heading.InnerHtml.AppendHtml(badge);
        }

        head.InnerHtml.AppendHtml(heading);

        if (!string.IsNullOrWhiteSpace(Subtitle))
        {
            var subtitle = new TagBuilder("p");
            subtitle.AddCssClass("ms-list__subtitle");
            subtitle.InnerHtml.Append(Subtitle!);
            head.InnerHtml.AppendHtml(subtitle);
        }

        return head;
    }
}
