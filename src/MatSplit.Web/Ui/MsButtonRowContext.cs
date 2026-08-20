using Microsoft.AspNetCore.Html;

namespace MatSplit.Web.Ui;

/// <summary>
/// Collector used by ms-button-row and ms-actions. ms-button registers itself
/// here so the parent can enforce the button order positive to negative
/// (primary, secondary, spacer, danger) regardless of the markup order.
/// </summary>
public sealed class MsButtonRowContext
{
    public List<IHtmlContent> Primary { get; } = [];

    public List<IHtmlContent> Secondary { get; } = [];

    public List<IHtmlContent> Danger { get; } = [];

    public void Add(MsButtonKind kind, IHtmlContent content)
    {
        switch (kind)
        {
            case MsButtonKind.Primary:
                Primary.Add(content);
                break;
            case MsButtonKind.Danger:
                Danger.Add(content);
                break;
            default:
                Secondary.Add(content);
                break;
        }
    }
}
