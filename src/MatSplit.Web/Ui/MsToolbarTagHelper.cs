using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace MatSplit.Web.Ui;

/// <summary>
/// Filter bar above a list. Renders a GET form containing the search, filter and
/// sort fields plus the submit and reset buttons. When placed inside a ms-list it
/// registers itself as the toolbar slot, so the position is enforced by the list.
/// </summary>
[HtmlTargetElement("ms-toolbar")]
public sealed class MsToolbarTagHelper : MsTagHelperBase
{
    protected override string ControlName => "toolbar";

    /// <summary>Form target, defaults to the current url.</summary>
    public string? Action { get; set; }

    /// <summary>Http method, get for filters.</summary>
    public string Method { get; set; } = "get";

    /// <summary>Label of the submit button.</summary>
    public string SubmitLabel { get; set; } = "Filtern";

    /// <summary>When set a reset link pointing to this url is rendered.</summary>
    public string? ResetUrl { get; set; }

    /// <summary>Label of the reset link.</summary>
    public string ResetLabel { get; set; } = "Zur\u00fccksetzen";

    /// <summary>Submits the form automatically on change (selects, dates, checkboxes).</summary>
    public bool AutoSubmit { get; set; } = true;

    /// <summary>Also auto submits while typing in text fields (debounced).</summary>
    public bool AutoSubmitText { get; set; }

    /// <summary>Adds a hidden field that resets the paging when filtering.</summary>
    public bool ResetPage { get; set; } = true;

    /// <summary>Name of the paging query parameter.</summary>
    public string PageParam { get; set; } = "page";

    /// <summary>Comma separated query keys that are carried over as hidden inputs.</summary>
    public string? Preserve { get; set; }

    /// <summary>Hides the submit button, only useful together with auto submit.</summary>
    public bool HideSubmit { get; set; }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(output);

        var id = ResolveId();
        var children = await output.GetChildContentAsync();

        var form = new TagBuilder("form");
        form.AddCssClass(MsHtml.Classes("ms-toolbar", CssClass));
        form.Attributes["id"] = id;
        form.Attributes["method"] = string.IsNullOrWhiteSpace(Method) ? "get" : Method.ToLowerInvariant();
        form.Attributes["role"] = "search";

        if (!string.IsNullOrWhiteSpace(Action))
        {
            form.Attributes["action"] = Action!;
        }

        if (AutoSubmit)
        {
            form.Attributes["data-ms-autosubmit"] = "true";
        }

        if (AutoSubmitText)
        {
            form.Attributes["data-ms-autosubmit-text"] = "true";
        }

        if (ResetPage)
        {
            var hidden = new TagBuilder("input")
            {
                TagRenderMode = TagRenderMode.SelfClosing
            };
            hidden.Attributes["type"] = "hidden";
            hidden.Attributes["name"] = PageParam;
            hidden.Attributes["value"] = "1";
            form.InnerHtml.AppendHtml(hidden);
        }

        MsHtml.AppendPreservedQuery(form, ViewContext, Preserve);

        var fields = new TagBuilder("div");
        fields.AddCssClass("ms-toolbar__fields");
        fields.InnerHtml.AppendHtml(children);
        form.InnerHtml.AppendHtml(fields);

        var buttons = new TagBuilder("div");
        buttons.AddCssClass("ms-toolbar__buttons");

        if (!HideSubmit)
        {
            var submit = new TagBuilder("button");
            submit.AddCssClass("ms-btn ms-btn--primary ms-btn--sm");
            submit.Attributes["type"] = "submit";
            submit.Attributes["id"] = id + "-submit";
            submit.InnerHtml.AppendHtml(MsHtml.Icon("search", 16));
            var submitText = new TagBuilder("span");
            submitText.InnerHtml.Append(SubmitLabel);
            submit.InnerHtml.AppendHtml(submitText);
            buttons.InnerHtml.AppendHtml(submit);
        }

        if (!string.IsNullOrWhiteSpace(ResetUrl))
        {
            var reset = new TagBuilder("a");
            reset.AddCssClass("ms-btn ms-btn--ghost ms-btn--sm");
            reset.Attributes["href"] = ResetUrl!;
            reset.Attributes["id"] = id + "-reset";
            reset.InnerHtml.AppendHtml(MsHtml.Icon("close", 16));
            var resetText = new TagBuilder("span");
            resetText.InnerHtml.Append(ResetLabel);
            reset.InnerHtml.AppendHtml(resetText);
            buttons.InnerHtml.AppendHtml(reset);
        }

        form.InnerHtml.AppendHtml(buttons);

        MsHtml.CopyAttributes(output, form);

        if (context.Items.TryGetValue(typeof(MsListContext), out var raw) && raw is MsListContext list)
        {
            list.Toolbar = form;
            output.SuppressOutput();
            return;
        }

        output.TagName = null;
        output.Content.SetHtmlContent(form);
    }
}
