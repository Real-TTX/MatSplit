using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace MatSplit.Web.Ui;

/// <summary>
/// Action bar below a list. Buttons are left aligned, destructive buttons are
/// separated and pushed to the right. Children are usually ms-button elements,
/// plain html is rendered after them.
/// </summary>
[HtmlTargetElement("ms-actions")]
public sealed class MsActionsTagHelper : MsTagHelperBase
{
    protected override string ControlName => "actions";

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(output);

        var id = ResolveId();
        var row = new MsButtonRowContext();
        context.Items[typeof(MsButtonRowContext)] = row;

        var freeContent = await output.GetChildContentAsync();

        var wrap = new TagBuilder("div");
        wrap.Attributes["id"] = id;
        wrap.AddCssClass(MsHtml.Classes("ms-actions", CssClass));

        foreach (var button in row.Primary)
        {
            wrap.InnerHtml.AppendHtml(button);
        }

        foreach (var button in row.Secondary)
        {
            wrap.InnerHtml.AppendHtml(button);
        }

        if (!freeContent.IsEmptyOrWhiteSpace)
        {
            wrap.InnerHtml.AppendHtml(freeContent);
        }

        if (row.Danger.Count > 0)
        {
            var danger = new TagBuilder("span");
            danger.AddCssClass("ms-actions__danger");

            foreach (var button in row.Danger)
            {
                danger.InnerHtml.AppendHtml(button);
            }

            wrap.InnerHtml.AppendHtml(danger);
        }

        MsHtml.CopyAttributes(output, wrap);

        IHtmlContent result = wrap;

        if (context.Items.TryGetValue(typeof(MsListContext), out var raw) && raw is MsListContext list)
        {
            list.Actions = result;
            output.SuppressOutput();
            return;
        }

        output.TagName = null;
        output.Content.SetHtmlContent(result);
    }
}
