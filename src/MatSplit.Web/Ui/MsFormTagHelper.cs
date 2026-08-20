using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace MatSplit.Web.Ui;

/// <summary>
/// Form wrapper for the edit pages. Adds the antiforgery token, the validation
/// summary and the form grid. Children are ms-field and ms-button-row elements
/// and are rendered in markup order.
/// </summary>
[HtmlTargetElement("ms-form")]
public sealed class MsFormTagHelper : MsTagHelperBase
{
    private readonly IAntiforgery _antiforgery;
    private readonly IHtmlGenerator _generator;

    public MsFormTagHelper(IAntiforgery antiforgery, IHtmlGenerator generator)
    {
        _antiforgery = antiforgery;
        _generator = generator;
    }

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

    /// <summary>
    /// Switches the client side validation off completely. The native browser
    /// bubbles are always off (site.js owns the validation so every message is
    /// German and lands in the validation summary).
    /// </summary>
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
        MsHtml.AddClass(output, MsHtml.Classes("ms-form", Columns >= 2 ? "ms-form--cols-2" : null, CssClass));

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

        // Native bubbles are always suppressed: site.js validates the same rules,
        // in German, and feeds the validation summary. The required/min/max
        // attributes stay in the markup for semantics and mobile keyboards.
        output.Attributes.SetAttribute("novalidate", "novalidate");

        if (NoValidate)
        {
            output.Attributes.SetAttribute("data-ms-novalidate", "true");
        }

        if (!string.IsNullOrWhiteSpace(Autocomplete))
        {
            output.Attributes.SetAttribute("autocomplete", Autocomplete!);
        }

        output.Attributes.SetAttribute("data-ms-form", "true");

        var mode = ValidationSummary?.Trim().ToLowerInvariant() ?? "all";

        if (!string.Equals(mode, "none", StringComparison.Ordinal))
        {
            var summary = _generator.GenerateValidationSummary(
                ViewContext,
                excludePropertyErrors: string.Equals(mode, "modelonly", StringComparison.Ordinal),
                message: null,
                headerTag: null,
                htmlAttributes: new { @class = "ms-alert ms-alert--error ms-form__summary", id = id + "-summary" });

            if (summary is not null)
            {
                output.PreContent.AppendHtml(summary);
            }
        }

        output.Content.SetHtmlContent(children);

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
}
