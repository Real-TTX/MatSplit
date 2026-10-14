using Microsoft.AspNetCore.Razor.TagHelpers;

namespace MatSplit.Web.Ui;

/// <summary>
/// Round avatar (<c>mat-avatar</c>) built from a member's initials. Purely decorative:
/// the name is expected to appear next to the avatar in the surrounding markup,
/// so the glyph is hidden from assistive tech and the full name is exposed via
/// the title attribute. Its colour is the one of the toolkit: no colour is made up per person.
/// </summary>
[HtmlTargetElement("ms-avatar")]
public sealed class MsAvatarTagHelper : MsTagHelperBase
{
    protected override string ControlName => "avatar";

    /// <summary>Display name the initials are derived from.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>sm, md (default) or lg.</summary>
    public string Size { get; set; } = "md";

    /// <summary>Marks the current user; adds a highlighted ring.</summary>
    public bool You { get; set; }

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(output);

        var size = (Size ?? "md").Trim().ToLowerInvariant();

        output.TagName = "span";
        output.TagMode = TagMode.StartTagAndEndTag;

        if (!string.IsNullOrWhiteSpace(Id))
        {
            output.Attributes.SetAttribute("id", Id!);
        }

        MsHtml.AddClass(output, MsHtml.Classes(
            "mat-avatar",
            size is "sm" or "lg" ? "mat-avatar--" + size : null,
            You ? "ms-avatar--you" : null,
            CssClass));

        output.Attributes.SetAttribute("aria-hidden", "true");

        if (!string.IsNullOrWhiteSpace(Name))
        {
            output.Attributes.SetAttribute("title", Name);
        }

        output.Content.SetContent(MsAvatar.Initials(Name));
    }
}
