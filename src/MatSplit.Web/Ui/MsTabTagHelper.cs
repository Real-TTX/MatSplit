using Microsoft.AspNetCore.Razor.TagHelpers;

namespace MatSplit.Web.Ui;

/// <summary>
/// One tab of a ms-tabs control. Registers itself with the parent; when used
/// without a parent the content is rendered as a plain block so a page never
/// loses content.
/// </summary>
[HtmlTargetElement("ms-tab")]
public sealed class MsTabTagHelper : MsTagHelperBase
{
    protected override string ControlName => "tab";

    /// <summary>Stable key used in the dom ids and for the active selection.</summary>
    public string? Key { get; set; }

    /// <summary>Caption of the tab.</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>Icon name from the sprite.</summary>
    public string? Icon { get; set; }

    /// <summary>Turns the tab into a navigation link.</summary>
    public string? Href { get; set; }

    /// <summary>Small counter badge.</summary>
    public string? Badge { get; set; }

    /// <summary>Marks this tab as the active one.</summary>
    public bool Active { get; set; }

    public bool Disabled { get; set; }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(output);

        var children = await output.GetChildContentAsync();

        if (!context.Items.TryGetValue(typeof(MsTabsContext), out var raw) || raw is not MsTabsContext tabs)
        {
            output.TagName = "div";
            output.TagMode = TagMode.StartTagAndEndTag;
            output.Attributes.SetAttribute("id", ResolveId());
            MsHtml.AddClass(output, MsHtml.Classes("ms-tabs__panel", "is-active", CssClass));
            output.Content.SetHtmlContent(children);
            return;
        }

        var index = tabs.Tabs.Count + 1;
        var key = MsControlIds.Slug(Key ?? Label, "tab" + index.ToString(System.Globalization.CultureInfo.InvariantCulture));

        tabs.Tabs.Add(new MsTabEntry
        {
            Key = key,
            Label = string.IsNullOrWhiteSpace(Label) ? key : Label,
            Icon = Icon,
            Href = Href,
            Badge = Badge,
            Disabled = Disabled,
            RequestedActive = Active,
            Content = children.IsEmptyOrWhiteSpace ? null : children
        });

        output.SuppressOutput();
    }
}
