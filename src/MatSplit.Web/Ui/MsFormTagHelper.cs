using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace MatSplit.Web.Ui;

/// <summary>
/// Form wrapper for the edit pages (<c>mat-form</c>). Adds the antiforgery token, the validation
/// summary and, for two columns, the field grid. Children are ms-field and ms-button-row elements
/// and are rendered in markup order. MatUi validates the form on submit and writes the messages
/// (<c>data-msg-*</c> of the fields) itself.
/// </summary>
[HtmlTargetElement("ms-form")]
public sealed class MsFormTagHelper : MsTagHelperBase
{
    private readonly IAntiforgery _antiforgery;

    public MsFormTagHelper(IAntiforgery antiforgery) => _antiforgery = antiforgery;

    protected override string ControlName => "form";

    /// <summary>Form target, defaults to the current url.</summary>
    public string? Action { get; set; }

    /// <summary>Http method, post by default.</summary>
    public string Method { get; set; } = "post";

    /// <summary>Named razor page handler, appended as ?handler=.</summary>
    public string? Handler { get; set; }

    /// <summary>Set to true when the form contains a file input.</summary>
    public bool HasFiles { get; set; }

    /// <summary>Explicit enctype, overrides has-files.</summary>
    public string? Enctype { get; set; }

    /// <summary>Renders the antiforgery token, on by default for post forms.</summary>
    public bool Antiforgery { get; set; } = true;

    /// <summary>all, modelonly or none.</summary>
    public string ValidationSummary { get; set; } = "all";

    /// <summary>Number of columns of the field grid (1 or 2).</summary>
    public int Columns { get; set; } = 1;

    /// <summary>Switches the client side validation off completely.</summary>
    public bool NoValidate { get; set; }

    /// <summary>Value of the autocomplete attribute.</summary>
    public string? Autocomplete { get; set; }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(output);

        var id = ResolveId();
        var children = await output.GetChildContentAsync();
        var method = string.IsNullOrWhiteSpace(Method) ? "post" : Method.ToLowerInvariant();
        var isPost = string.Equals(method, "post", StringComparison.Ordinal);

        output.TagName = "form";
        output.TagMode = TagMode.StartTagAndEndTag;
        output.Attributes.SetAttribute("id", id);
        output.Attributes.SetAttribute("method", method);
        MsHtml.AddClass(output, MsHtml.Classes("mat-form", CssClass));

        var action = Action;

        if (!string.IsNullOrWhiteSpace(Handler))
        {
            var basePath = string.IsNullOrWhiteSpace(action)
                ? ViewContext.HttpContext.Request.Path.Value ?? "/"
                : action!;
            var separator = basePath.Contains('?', StringComparison.Ordinal) ? "&" : "?";
            action = basePath + separator + "handler=" + Uri.EscapeDataString(Handler!);
        }

        if (!string.IsNullOrWhiteSpace(action))
        {
            output.Attributes.SetAttribute("action", action!);
        }

        var enctype = Enctype;

        if (string.IsNullOrWhiteSpace(enctype) && HasFiles)
        {
            enctype = "multipart/form-data";
        }

        if (!string.IsNullOrWhiteSpace(enctype))
        {
            output.Attributes.SetAttribute("enctype", enctype!);
        }

        // The native bubbles are always off: MatUi checks the same rules, in German, and feeds the summary.
        output.Attributes.SetAttribute("novalidate", "novalidate");

        if (NoValidate)
        {
            // Opt out of the check of MatUi for this form (only forms with mat-form or data-validate are checked).
            output.Attributes.SetAttribute("data-ms-novalidate", "true");
        }

        if (!string.IsNullOrWhiteSpace(Autocomplete))
        {
            output.Attributes.SetAttribute("autocomplete", Autocomplete!);
        }

        var mode = ValidationSummary?.Trim().ToLowerInvariant() ?? "all";

        if (!string.Equals(mode, "none", StringComparison.Ordinal))
        {
            output.PreContent.AppendHtml(BuildSummary(id, string.Equals(mode, "modelonly", StringComparison.Ordinal)));
        }

        if (Columns >= 2)
        {
            var grid = new TagBuilder("div");
            grid.AddCssClass("mat-form__grid");
            grid.InnerHtml.AppendHtml(children);
            output.Content.SetHtmlContent(grid);
        }
        else
        {
            output.Content.SetHtmlContent(children);
        }

        if (isPost && Antiforgery)
        {
            var tokens = _antiforgery.GetAndStoreTokens(ViewContext.HttpContext);
            var hidden = new TagBuilder("input")
            {
                TagRenderMode = TagRenderMode.SelfClosing
            };
            hidden.Attributes["type"] = "hidden";
            hidden.Attributes["name"] = tokens.FormFieldName;
            hidden.Attributes["value"] = tokens.RequestToken ?? string.Empty;
            output.PostContent.AppendHtml(hidden);
        }
    }

    /// <summary>
    /// The summary at the top of the form (FORM-03): an alert that lists what the server did not accept.
    /// MatUi fills the same box, with a link to every field, with what it finds invalid on submit.
    /// </summary>
    private TagBuilder BuildSummary(string id, bool modelOnly)
    {
        var errors = new List<(string Key, string Message)>();

        foreach (var (key, entry) in ViewContext.ViewData.ModelState)
        {
            if (modelOnly && !string.IsNullOrEmpty(key))
            {
                continue;
            }

            foreach (var error in entry.Errors)
            {
                if (!string.IsNullOrWhiteSpace(error.ErrorMessage))
                {
                    errors.Add((key, error.ErrorMessage));
                }
            }
        }

        var box = new TagBuilder("div");
        box.AddCssClass("mat-alert mat-alert--danger");
        box.Attributes["id"] = id + "-summary";
        box.Attributes["role"] = "alert";
        box.Attributes["data-validation-summary"] = "true";

        if (errors.Count == 0)
        {
            box.Attributes["hidden"] = "hidden";
        }

        box.InnerHtml.AppendHtml(MsHtml.Icon("x-circle"));

        var body = new TagBuilder("div");
        body.AddCssClass("mat-alert__body");

        var title = new TagBuilder("strong");
        title.InnerHtml.Append("Bitte prüfe die Eingaben.");
        body.InnerHtml.AppendHtml(title);

        var list = new TagBuilder("ul");
        list.AddCssClass("mat-alert__list");

        foreach (var (_, message) in errors)
        {
            var item = new TagBuilder("li");
            item.InnerHtml.Append(message);
            list.InnerHtml.AppendHtml(item);
        }

        body.InnerHtml.AppendHtml(list);
        box.InnerHtml.AppendHtml(body);
        return box;
    }
}
