using System.Globalization;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace MatSplit.Web.Ui;

/// <summary>
/// Inline svg icon referencing the sprite that the layout renders once per page.
/// Decorative by default; pass a label to make it readable for screen readers.
/// </summary>
[HtmlTargetElement("ms-icon")]
public sealed class MsIconTagHelper : MsTagHelperBase
{
    protected override string ControlName => "icon";

    /// <summary>Symbol name, e.g. group, expense, trash.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Edge length in pixels.</summary>
    public int Size { get; set; } = 20;

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

        var size = Size <= 0 ? 20 : Size;

        output.TagName = "svg";
        output.TagMode = TagMode.StartTagAndEndTag;
        MsHtml.AddClass(output, MsHtml.Classes("ms-icon", CssClass));
        output.Attributes.SetAttribute("width", size.ToString(CultureInfo.InvariantCulture));
        output.Attributes.SetAttribute("height", size.ToString(CultureInfo.InvariantCulture));
        output.Attributes.SetAttribute("viewBox", "0 0 24 24");
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

        output.Content.SetHtmlContent("<use href=\"#ms-i-" + Name.Trim() + "\"></use>");
    }
}
