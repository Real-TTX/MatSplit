using Microsoft.AspNetCore.Razor.TagHelpers;

namespace MatSplit.Web.Ui;

/// <summary>
/// Round avatar built from a member's initials with a deterministic background
/// colour derived from the name (see <see cref="MsAvatar"/>). Purely decorative:
/// the name is expected to appear next to the avatar in the surrounding markup,
/// so the glyph is hidden from assistive tech and the full name is exposed via
/// the title attribute.
/// </summary>
[HtmlTargetElement("ms-avatar")]
public sealed class MsAvatarTagHelper : MsTagHelperBase
{
    protected override string ControlName => "avatar";

    /// <summary>Display name the initials and colour are derived from.</summary>
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
        size = size is "sm" or "md" or "lg" ? size : "md";

        output.TagName = "span";
        output.TagMode = TagMode.StartTagAndEndTag;

        if (!string.IsNullOrWhiteSpace(Id))
        {
            output.Attributes.SetAttribute("id", Id!);
        }

        MsHtml.AddClass(output, MsHtml.Classes(
            "ms-avatar",
            "ms-avatar--" + size,
            You ? "ms-avatar--you" : null,
            CssClass));

        output.Attributes.SetAttribute("style", "--ms-avatar-bg: " + MsAvatar.Background(Name));
        output.Attributes.SetAttribute("aria-hidden", "true");

        if (!string.IsNullOrWhiteSpace(Name))
        {
            output.Attributes.SetAttribute("title", Name);
        }

        output.Content.SetContent(MsAvatar.Initials(Name));
    }
}
