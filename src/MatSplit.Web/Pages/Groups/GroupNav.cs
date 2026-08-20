namespace MatSplit.Web.Pages.Groups;

/// <summary>
/// Model of the shared _GroupNav partial (the tab bar above every group sub
/// page). Render it with
/// <c>&lt;partial name="_GroupNav" model="new GroupNav(Model.GroupId, GroupNav.Expenses)" /&gt;</c>.
/// </summary>
/// <param name="GroupId">Group the tabs point to.</param>
/// <param name="Active">Key of the active tab, use the constants of this record.</param>
/// <param name="CanManage">True for group admins, adds the settings tab.</param>
public sealed record GroupNav(long GroupId, string Active, bool CanManage = false)
{
    /// <summary>
    /// Renders the tab strip as horizontally scrollable pills instead of the
    /// default underline tabs. Only takes effect on mobile (&lt;= 900px); used by
    /// the group dashboard to match the mockup. Desktop stays as underline tabs.
    /// </summary>
    public bool Pills { get; init; }

    /// <summary>Tab key of /Groups/Details.</summary>
    public const string Details = "details";

    /// <summary>Tab key of /Groups/Expenses.</summary>
    public const string Expenses = "expenses";

    /// <summary>Tab key of /Groups/Payments.</summary>
    public const string Payments = "payments";

    /// <summary>Tab key of /Groups/Balance.</summary>
    public const string Balance = "balance";

    /// <summary>Tab key of /Groups/History.</summary>
    public const string History = "history";

    /// <summary>Tab key of /Groups/Members.</summary>
    public const string Members = "members";

    /// <summary>Tab key of /Groups/Edit (group settings, admins only).</summary>
    public const string Settings = "settings";
}
