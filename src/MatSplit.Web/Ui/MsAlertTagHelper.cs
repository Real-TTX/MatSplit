using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace MatSplit.Web.Ui;

/// <summary>
/// MatUi alert (<c>mat-alert</c>): an inline message for hints, success and error messages.
/// Every alert has an icon and a lead, so the colour is never the only signal.
/// </summary>
[HtmlTargetElement("ms-alert")]
public sealed class MsAlertTagHelper : MsTagHelperBase
{
    protected override string ControlName => "alert";

    /// <summary>info, success, warning or error.</summary>
    public string Tone { get; set; } = "info";

    /// <summary>Optional bold headline.</summary>
    public string? Title { get; set; }

    /// <summary>Message text, alternatively use the child content.</summary>
    public string? Text { get; set; }

    /// <summary>Overrides the icon derived from the tone.</summary>
    public string? Icon { get; set; }

    /// <summary>Adds a close button.</summary>
    public bool Dismissible { get; set; }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(output);

        var id = ResolveId();
        var children = await output.GetChildContentAsync();
        var tone = (Tone ?? "info").Trim().ToLowerInvariant();

        output.TagName = "div";
        output.TagMode = TagMode.StartTagAndEndTag;
        output.Attributes.SetAttribute("id", id);
        output.Attributes.SetAttribute("role", tone is "error" or "danger" or "warning" ? "alert" : "status");
        MsHtml.AddClass(output, MsHtml.Classes("mat-alert", "mat-alert--" + ToneClass(tone), CssClass));

        var icon = string.IsNullOrWhiteSpace(Icon) ? DefaultIcon(tone) : Icon!;
        output.PreContent.AppendHtml(MsHtml.Icon(icon));

        var body = new TagBuilder("div");
        body.AddCssClass("mat-alert__body");

        if (!string.IsNullOrWhiteSpace(Title))
        {
            var title = new TagBuilder("strong");
            title.AddCssClass("mat-alert__title");
            title.InnerHtml.Append(Title!);
            body.InnerHtml.AppendHtml(title);
        }

        if (!string.IsNullOrWhiteSpace(Text))
        {
            var text = new TagBuilder("span");
            text.InnerHtml.Append(Text!);
            body.InnerHtml.AppendHtml(text);
        }

        if (MsHtml.HasContent(children))
        {
            body.InnerHtml.AppendHtml(children);
        }

        output.Content.SetHtmlContent(body);

        if (!Dismissible)
        {
            return;
        }

        var close = new TagBuilder("button");
        close.AddCssClass("mat-alert__close");
        close.Attributes["type"] = "button";
        close.Attributes["id"] = id + "-close";
        close.Attributes["data-dismiss"] = "true";
        close.Attributes["aria-label"] = "Meldung schließen";
        close.InnerHtml.AppendHtml(MsHtml.Icon("x"));
        output.PostContent.AppendHtml(close);
    }

    private static string ToneClass(string tone) => tone switch
    {
        "error" => "danger",
        "success" or "warning" or "danger" => tone,
        _ => "info"
    };

    private static string DefaultIcon(string tone) => tone switch
    {
        "success" => "check-circle",
        "warning" => "alert",
        "error" or "danger" => "x-circle",
        _ => "info"
    };
}
