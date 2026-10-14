using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace MatSplit.Web.Ui;

/// <summary>
/// MatUi button (<c>mat-btn</c>) or link with icon. Inside ms-button-row / ms-actions the button
/// registers itself with its parent so the parent can enforce the order
/// positive to negative; outside it renders in place.
/// </summary>
[HtmlTargetElement("ms-button")]
public sealed class MsButtonTagHelper : MsTagHelperBase
{
    protected override string ControlName => "button";

    /// <summary>primary, secondary, ghost or danger.</summary>
    public string Kind { get; set; } = "secondary";

    /// <summary>Button caption, alternatively use the child content.</summary>
    public string? Label { get; set; }

    /// <summary>Icon name from the sprite.</summary>
    public string? Icon { get; set; }

    /// <summary>When set an anchor is rendered instead of a button.</summary>
    public string? Href { get; set; }

    /// <summary>submit, button or reset. Defaults to submit for buttons.</summary>
    public string Type { get; set; } = "submit";

    /// <summary>Name of the submit button, used for razor page handlers.</summary>
    public string? Name { get; set; }

    /// <summary>Value of the submit button.</summary>
    public string? Value { get; set; }

    /// <summary>Overrides the form action of this button.</summary>
    public string? FormAction { get; set; }

    /// <summary>Id of the form this button submits, for buttons outside the form.</summary>
    public string? Form { get; set; }

    /// <summary>Razor page handler, rendered as formaction=?handler=name.</summary>
    public string? Handler { get; set; }

    /// <summary>Confirmation question shown in a MatUi dialog before the action runs.</summary>
    public string? Confirm { get; set; }

    public bool Disabled { get; set; }

    /// <summary>sm or md.</summary>
    public string? Size { get; set; }

    /// <summary>Renders the button full width, used on mobile forms.</summary>
    [HtmlAttributeName("full-width")]
    public bool FullWidth { get; set; }

    /// <summary>Icon only button, the label becomes the aria-label.</summary>
    [HtmlAttributeName("icon-only")]
    public bool IconOnly { get; set; }

    /// <summary>Target attribute for links.</summary>
    public string? Target { get; set; }

    /// <summary>Title / tooltip.</summary>
    public string? Title { get; set; }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(output);

        var id = ResolveId();
        var children = await output.GetChildContentAsync();
        var kind = ParseKind(Kind);
        var isLink = !string.IsNullOrWhiteSpace(Href);

        var tag = new TagBuilder(isLink ? "a" : "button");
        tag.Attributes["id"] = id;
        tag.AddCssClass(MsHtml.Classes(
            "mat-btn",
            "mat-btn--" + KindClass(kind),
            string.Equals(Size, "sm", StringComparison.OrdinalIgnoreCase) ? "mat-btn--sm" : null,
            string.Equals(Size, "lg", StringComparison.OrdinalIgnoreCase) ? "mat-btn--lg" : null,
            FullWidth ? "mat-btn--block" : null,
            IconOnly ? "mat-btn--icon" : null,
            CssClass));

        if (isLink)
        {
            tag.Attributes["href"] = Disabled ? "#" : Href!;

            if (Disabled)
            {
                tag.Attributes["aria-disabled"] = "true";
                tag.AddCssClass("is-disabled");
            }

            if (!string.IsNullOrWhiteSpace(Target))
            {
                tag.Attributes["target"] = Target!;
                tag.Attributes["rel"] = "noopener";
            }
        }
        else
        {
            tag.Attributes["type"] = string.IsNullOrWhiteSpace(Type) ? "submit" : Type!;

            if (!string.IsNullOrWhiteSpace(Name))
            {
                tag.Attributes["name"] = Name!;
            }

            if (Value is not null)
            {
                tag.Attributes["value"] = Value;
            }

            if (!string.IsNullOrWhiteSpace(Form))
            {
                tag.Attributes["form"] = Form!;
            }

            var formAction = FormAction;

            if (string.IsNullOrWhiteSpace(formAction) && !string.IsNullOrWhiteSpace(Handler))
            {
                var path = ViewContext.HttpContext.Request.Path.Value ?? "/";
                var queryString = ViewContext.HttpContext.Request.QueryString.Value ?? string.Empty;
                var separator = string.IsNullOrEmpty(queryString) ? "?" : "&";
                formAction = path + queryString + separator + "handler=" + Uri.EscapeDataString(Handler!);
            }

            if (!string.IsNullOrWhiteSpace(formAction))
            {
                tag.Attributes["formaction"] = formAction!;
            }

            // Delete and the other buttons that name a handler send the form without the check of the fields (FORM-05).
            if (kind == MsButtonKind.Danger || !string.IsNullOrWhiteSpace(formAction))
            {
                tag.Attributes["formnovalidate"] = "formnovalidate";
            }

            if (Disabled)
            {
                tag.Attributes["disabled"] = "disabled";
            }
        }

        var caption = Label;

        if (string.IsNullOrWhiteSpace(caption) && MsHtml.HasContent(children))
        {
            caption = children.GetContent().Trim();
        }

        if (!string.IsNullOrWhiteSpace(Confirm))
        {
            // A dialog of MatUi instead of window.confirm (DIALOG-01); the red button names the action.
            tag.Attributes["data-confirm"] = Confirm!;

            if (kind == MsButtonKind.Danger && !string.IsNullOrWhiteSpace(caption))
            {
                tag.Attributes["data-confirm-ok"] = caption!;
            }
        }

        if (!string.IsNullOrWhiteSpace(Title))
        {
            tag.Attributes["title"] = Title!;
        }

        if (!string.IsNullOrWhiteSpace(Icon))
        {
            tag.InnerHtml.AppendHtml(MsHtml.Icon(Icon));
        }

        if (IconOnly)
        {
            if (!string.IsNullOrWhiteSpace(caption))
            {
                tag.Attributes["aria-label"] = caption!;
                tag.Attributes["data-tip"] = caption!;
            }
        }
        else if (!string.IsNullOrWhiteSpace(caption))
        {
            var span = new TagBuilder("span");
            span.InnerHtml.Append(caption!);
            tag.InnerHtml.AppendHtml(span);
        }

        MsHtml.CopyAttributes(output, tag);

        if (context.Items.TryGetValue(typeof(MsButtonRowContext), out var raw) && raw is MsButtonRowContext row)
        {
            row.Add(kind, tag);
            output.SuppressOutput();
            return;
        }

        output.TagName = null;
        output.Content.SetHtmlContent(tag);
    }

    private static MsButtonKind ParseKind(string? kind) => kind?.Trim().ToLowerInvariant() switch
    {
        "primary" or "save" or "positive" => MsButtonKind.Primary,
        "danger" or "delete" or "destructive" => MsButtonKind.Danger,
        "ghost" or "link" => MsButtonKind.Ghost,
        _ => MsButtonKind.Secondary
    };

    private static string KindClass(MsButtonKind kind) => kind switch
    {
        MsButtonKind.Primary => "primary",
        MsButtonKind.Danger => "danger",
        MsButtonKind.Ghost => "ghost",
        _ => "secondary"
    };
}
