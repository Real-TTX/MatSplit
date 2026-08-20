using Microsoft.AspNetCore.Razor.TagHelpers;

namespace MatSplit.Web.Ui;

/// <summary>
/// Renders an amount stored in cents as a German currency string. Negative
/// amounts get an extra css class so lists can colour debts.
/// </summary>
[HtmlTargetElement("ms-money")]
public sealed class MsMoneyTagHelper : MsTagHelperBase
{
    protected override string ControlName => "money";

    /// <summary>Amount in cents.</summary>
    public long Cents { get; set; }

    /// <summary>Currency code, EUR by default.</summary>
    public string Currency { get; set; } = "EUR";

    /// <summary>
    /// Adds a plus sign for positive amounts and colours them green. Use it for
    /// balances; plain amounts stay in the normal text colour (negative values
    /// are always red).
    /// </summary>
    [HtmlAttributeName("show-sign")]
    public bool ShowSign { get; set; }

    /// <summary>Renders the value in a slightly larger, bold style.</summary>
    public bool Strong { get; set; }

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(output);

        output.TagName = "span";
        output.TagMode = TagMode.StartTagAndEndTag;

        if (!string.IsNullOrWhiteSpace(Id))
        {
            output.Attributes.SetAttribute("id", Id!);
        }

        MsHtml.AddClass(output, MsHtml.Classes(
            "ms-money",
            Cents < 0 ? "is-negative" : null,
            ShowSign && Cents > 0 ? "is-positive" : null,
            Strong ? "is-strong" : null,
            CssClass));

        var text = MsHtml.FormatMoney(Cents, Currency);

        if (ShowSign && Cents > 0)
        {
            text = "+" + text;
        }

        output.Content.SetContent(text);
    }
}
