using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace MatSplit.Web.Ui;

/// <summary>
/// Button row of a form (<c>mat-btn-row</c>). Enforces the mandatory order positive to negative:
/// primary buttons (Save) first, then the neutral buttons (Back), then a gap and
/// finally the destructive buttons (Delete). The markup order inside the row does not matter.
/// </summary>
[HtmlTargetElement("ms-button-row")]
public sealed class MsButtonRowTagHelper : MsTagHelperBase
{
    protected override string ControlName => "buttonrow";

    /// <summary>Adds a separating line above the row.</summary>
    public bool Divider { get; set; } = true;

    /// <summary>Sticks the row to the bottom of the viewport on mobile.</summary>
    public bool Sticky { get; set; }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(output);

        var id = ResolveId();
        var row = new MsButtonRowContext();
        context.Items[typeof(MsButtonRowContext)] = row;

        var freeContent = await output.GetChildContentAsync();

        output.TagName = "div";
        output.TagMode = TagMode.StartTagAndEndTag;
        output.Attributes.SetAttribute("id", id);
        MsHtml.AddClass(output, MsHtml.Classes(
            "mat-btn-row",
            Divider ? null : "mat-btn-row--plain",
            Sticky ? "mat-btn-row--sticky" : null,
            CssClass));

        output.Content.Clear();

        foreach (var button in row.Primary)
        {
            output.Content.AppendHtml(button);
        }

        foreach (var button in row.Secondary)
        {
            output.Content.AppendHtml(button);
        }

        if (!freeContent.IsEmptyOrWhiteSpace)
        {
            output.Content.AppendHtml(freeContent);
        }

        if (row.Danger.Count == 0)
        {
            return;
        }

        var spacer = new TagBuilder("span");
        spacer.AddCssClass("mat-btn-row__spacer");
        output.Content.AppendHtml(spacer);

        foreach (var button in row.Danger)
        {
            output.Content.AppendHtml(button);
        }
    }
}
