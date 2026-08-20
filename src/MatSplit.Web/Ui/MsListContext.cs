using Microsoft.AspNetCore.Html;

namespace MatSplit.Web.Ui;

/// <summary>
/// Slot collector of ms-list. The child controls (ms-toolbar, ms-table,
/// ms-actions, ms-pagination) register their markup here instead of rendering
/// in place, which lets ms-list enforce the mandatory order
/// toolbar - table - pagination - actions.
/// </summary>
public sealed class MsListContext
{
    public MsListContext(string listId) => ListId = listId;

    /// <summary>Id of the surrounding ms-list.</summary>
    public string ListId { get; }

    public IHtmlContent? Toolbar { get; set; }

    public IHtmlContent? Table { get; set; }

    public IHtmlContent? Pagination { get; set; }

    public IHtmlContent? Actions { get; set; }

    public IHtmlContent? EmptyState { get; set; }
}
