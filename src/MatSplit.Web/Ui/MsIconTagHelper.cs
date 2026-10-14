using Microsoft.AspNetCore.Razor.TagHelpers;

namespace MatSplit.Web.Ui;

/// <summary>
/// Inline svg icon (<c>mat-icon</c>) referencing the sprites that the layout renders once per page
/// (the toolkit sprite and the app sprite). Decorative by default; pass a label to make it readable
/// for screen readers. The toolkit sizes the icon: the old size attribute is accepted and ignored.
/// </summary>
[HtmlTargetElement("ms-icon")]
public sealed class MsIconTagHelper : MsTagHelperBase
{
    protected override string ControlName => "icon";

    /// <summary>Symbol name, e.g. group, expense, trash.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Accepted for the pages that name it; MatUi sizes icons by the font size around them.</summary>
    public int Size { get; set; }

    /// <summary>Accessible name; when set the icon is exposed to screen readers.</summary>
    public string? Label { get; set; }

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(output);

        if (string.IsNullOrWhiteSpace(Name))
        {
            output.SuppressOutput();
            return;
        }

        output.TagName = "svg";
        output.TagMode = TagMode.StartTagAndEndTag;
        MsHtml.AddClass(output, MsHtml.Classes("mat-icon", CssClass));
        output.Attributes.SetAttribute("focusable", "false");

        if (!string.IsNullOrWhiteSpace(Id))
        {
            output.Attributes.SetAttribute("id", Id!);
        }

        if (string.IsNullOrWhiteSpace(Label))
        {
            output.Attributes.SetAttribute("aria-hidden", "true");
        }
        else
        {
            output.Attributes.SetAttribute("role", "img");
            output.Attributes.SetAttribute("aria-label", Label!);
        }

        output.Content.SetHtmlContent("<use href=\"#" + MsHtml.IconId(Name) + "\"></use>");
    }
}
