namespace MatSplit.Web.Ui;

/// <summary>Visual weight of a button, also drives its position in a button row.</summary>
public enum MsButtonKind
{
    /// <summary>Neutral action, rendered after the primary buttons.</summary>
    Secondary = 0,

    /// <summary>Main action such as Save, rendered first.</summary>
    Primary = 1,

    /// <summary>Borderless action, rendered with the secondary buttons.</summary>
    Ghost = 2,

    /// <summary>Destructive action, always rendered last and pushed to the right.</summary>
    Danger = 3
}
