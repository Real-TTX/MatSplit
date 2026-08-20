using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace MatSplit.Web.Ui;

/// <summary>
/// Base class of every ms-* control. Provides the mandatory id attribute
/// (auto generated when the page omits it) and access to the view context.
/// The control id prefixes all inner dom ids so that a control can be used
/// several times on the same page without id collisions.
/// </summary>
public abstract class MsTagHelperBase : TagHelper
{
    /// <summary>Unique control id, used as prefix for all inner dom ids.</summary>
    [HtmlAttributeName("id")]
    public string? Id { get; set; }

    /// <summary>Additional css classes appended to the control root element.</summary>
    [HtmlAttributeName("class")]
    public string? CssClass { get; set; }

    [HtmlAttributeNotBound]
    [ViewContext]
    public ViewContext ViewContext { get; set; } = default!;

    /// <summary>Short name used for generated ids, for example "list".</summary>
    protected abstract string ControlName { get; }

    /// <summary>Returns the explicit id or creates a request unique one.</summary>
    protected string ResolveId()
    {
        if (!string.IsNullOrWhiteSpace(Id))
        {
            return Id!;
        }

        Id = MsControlIds.Next(ViewContext, ControlName);
        return Id;
    }

    /// <summary>Builds an inner dom id prefixed with the control id.</summary>
    protected string InnerId(string suffix) => $"{ResolveId()}-{suffix}";
}
