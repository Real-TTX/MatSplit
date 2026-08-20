namespace MatSplit.Web.Ui;

/// <summary>
/// One entry of the breadcrumb below the page header. The last entry is
/// rendered as plain text even when it carries a url.
/// </summary>
/// <param name="Text">Visible label.</param>
/// <param name="Url">Target, null for a non clickable entry.</param>
/// <param name="Icon">Optional icon name from the icon sprite.</param>
public sealed record BreadcrumbItem(string Text, string? Url = null, string? Icon = null);
