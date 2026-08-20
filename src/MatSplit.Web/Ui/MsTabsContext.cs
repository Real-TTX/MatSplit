using Microsoft.AspNetCore.Html;

namespace MatSplit.Web.Ui;

/// <summary>Collector that ms-tab uses to register itself with its ms-tabs parent.</summary>
public sealed class MsTabsContext
{
    public MsTabsContext(string tabsId, string? activeKey)
    {
        TabsId = tabsId;
        ActiveKey = activeKey;
    }

    public string TabsId { get; }

    public string? ActiveKey { get; }

    public List<MsTabEntry> Tabs { get; } = [];
}

/// <summary>One registered tab including its panel content.</summary>
public sealed class MsTabEntry
{
    public required string Key { get; init; }

    public required string Label { get; init; }

    public string? Icon { get; init; }

    public string? Href { get; init; }

    public string? Badge { get; init; }

    public bool Disabled { get; init; }

    public bool RequestedActive { get; init; }

    public IHtmlContent? Content { get; init; }
}
