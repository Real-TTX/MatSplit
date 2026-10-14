using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace MatSplit.Web.Ui;

/// <summary>
/// Collector of ms-toolbar. The ms-field elements inside it do not render in place: the toolbar puts the search
/// field into the bar and every other field into the Filter dialog (the field called sort into the Sort dialog),
/// the way MatUi lays out a list toolbar (LIST-03). Every field belongs to the one GET form of the toolbar.
/// </summary>
public sealed class MsToolbarContext
{
    public MsToolbarContext(string formId) => FormId = formId;

    /// <summary>Id of the hidden GET form that every field of the toolbar belongs to (the form attribute).</summary>
    public string FormId { get; }

    public List<MsToolbarField> Fields { get; } = [];
}

/// <summary>A field of the toolbar as the ms-field element described it.</summary>
public sealed record MsToolbarField(
    string Id,
    string Name,
    string Label,
    string Type,
    string? Placeholder,
    IHtmlContent Html,
    IReadOnlyList<SelectListItem>? Items);
