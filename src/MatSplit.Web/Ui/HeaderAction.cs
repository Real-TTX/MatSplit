namespace MatSplit.Web.Ui;

/// <summary>
/// An icon at the right end of the header bar (SHELL-17): a page names what belongs to the whole page, such as
/// the history or the settings of a group. The main action of the page is the one icon in the accent colour,
/// and it is the last one.
/// </summary>
public sealed record HeaderAction(string Url, string Icon, string Label, bool Primary = false);
