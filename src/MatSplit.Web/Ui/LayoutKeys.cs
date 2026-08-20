namespace MatSplit.Web.Ui;

/// <summary>
/// ViewData / TempData keys the shared layout reads. Page models fill them,
/// the layout renders them. Use the helpers in <see cref="PageLayoutExtensions"/>
/// instead of writing the raw strings.
/// </summary>
public static class LayoutKeys
{
    /// <summary>Page title, rendered in the header and the browser title.</summary>
    public const string Title = "Title";

    /// <summary>Optional line below the page title.</summary>
    public const string Subtitle = "Subtitle";

    /// <summary>Application name, defaults to "MatSplit".</summary>
    public const string AppName = "AppName";

    /// <summary>Icon name of the page (see the icon sprite), rendered next to the title.</summary>
    public const string TitleIcon = "TitleIcon";

    /// <summary><see cref="IReadOnlyList{T}"/> of <see cref="BreadcrumbItem"/>.</summary>
    public const string Breadcrumb = "Breadcrumb";

    /// <summary><see cref="IEnumerable{T}"/> of <see cref="MenuGroupEntry"/> for the left menu.</summary>
    public const string MenuGroups = "MenuGroups";

    /// <summary>Id of the group whose sub menu is expanded (long).</summary>
    public const string ActiveGroupId = "ActiveGroupId";

    /// <summary>Overrides the display name shown at the bottom of the menu (string).</summary>
    public const string CurrentUserName = "CurrentUserName";

    /// <summary>Overrides the admin detection for the menu (bool).</summary>
    public const string IsAdmin = "IsAdmin";

    /// <summary>Hides the left menu, used by Login / Join (bool).</summary>
    public const string HideMenu = "HideMenu";

    /// <summary>Success message, survives a redirect when put into TempData.</summary>
    public const string Flash = "Flash";

    /// <summary>Error message, survives a redirect when put into TempData.</summary>
    public const string FlashError = "FlashError";
}
