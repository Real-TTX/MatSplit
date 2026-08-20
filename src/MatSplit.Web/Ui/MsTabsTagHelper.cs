using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace MatSplit.Web.Ui;

/// <summary>
/// Tab strip. Child ms-tab elements either carry their own content (in page
/// switching without a reload) or a href (navigation tabs, e.g. the group sub
/// pages). Mixed usage is allowed.
/// </summary>
[HtmlTargetElement("ms-tabs")]
public sealed class MsTabsTagHelper : MsTagHelperBase
{
    protected override string ControlName => "tabs";

    /// <summary>Key of the initially active tab.</summary>
    public string? Active { get; set; }

    /// <summary>Remembers the last active tab per control id in sessionStorage.</summary>
    public bool Remember { get; set; }

    /// <summary>Accessible name of the tab list.</summary>
    public string Label { get; set; } = "Bereiche";

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(output);

        var id = ResolveId();
        var tabs = new MsTabsContext(id, Active);
        context.Items[typeof(MsTabsContext)] = tabs;

        await output.GetChildContentAsync();

        output.TagName = "div";
        output.TagMode = TagMode.StartTagAndEndTag;
        output.Attributes.SetAttribute("id", id);
        output.Attributes.SetAttribute("data-ms-tabs", "true");
        MsHtml.AddClass(output, MsHtml.Classes("ms-tabs", CssClass));

        if (Remember)
        {
            output.Attributes.SetAttribute("data-ms-tabs-remember", "true");
        }

        var activeIndex = ResolveActiveIndex(tabs);

        var nav = new TagBuilder("div");
        nav.AddCssClass("ms-tabs__nav");
        nav.Attributes["role"] = "tablist";
        nav.Attributes["aria-label"] = Label;

        var panels = new TagBuilder("div");
        panels.AddCssClass("ms-tabs__panels");

        for (var index = 0; index < tabs.Tabs.Count; index++)
        {
            var tab = tabs.Tabs[index];
            var isActive = index == activeIndex;
            var tabId = id + "-tab-" + tab.Key;
            var panelId = id + "-panel-" + tab.Key;
            var isNavigation = !string.IsNullOrWhiteSpace(tab.Href);

            var button = new TagBuilder(isNavigation ? "a" : "button");
            button.Attributes["id"] = tabId;
            button.AddCssClass(MsHtml.Classes("ms-tabs__tab", isActive ? "is-active" : null, tab.Disabled ? "is-disabled" : null));

            if (isNavigation)
            {
                button.Attributes["href"] = tab.Href!;

                if (isActive)
                {
                    button.Attributes["aria-current"] = "page";
                }
            }
            else
            {
                button.Attributes["type"] = "button";
                button.Attributes["role"] = "tab";
                button.Attributes["aria-controls"] = panelId;
                button.Attributes["aria-selected"] = isActive ? "true" : "false";
                button.Attributes["data-ms-tab-target"] = panelId;

                if (!isActive)
                {
                    button.Attributes["tabindex"] = "-1";
                }

                if (tab.Disabled)
                {
                    button.Attributes["disabled"] = "disabled";
                }
            }

            if (!string.IsNullOrWhiteSpace(tab.Icon))
            {
                button.InnerHtml.AppendHtml(MsHtml.Icon(tab.Icon, 18));
            }

            var caption = new TagBuilder("span");
            caption.InnerHtml.Append(tab.Label);
            button.InnerHtml.AppendHtml(caption);

            if (!string.IsNullOrWhiteSpace(tab.Badge))
            {
                var badge = new TagBuilder("span");
                badge.AddCssClass("ms-badge");
                badge.InnerHtml.Append(tab.Badge!);
                button.InnerHtml.AppendHtml(badge);
            }

            nav.InnerHtml.AppendHtml(button);

            if (isNavigation && tab.Content is null)
            {
                continue;
            }

            var panel = new TagBuilder("section");
            panel.Attributes["id"] = panelId;
            panel.Attributes["role"] = "tabpanel";
            panel.Attributes["aria-labelledby"] = tabId;
            panel.AddCssClass(MsHtml.Classes("ms-tabs__panel", isActive ? "is-active" : null));

            if (!isActive)
            {
                panel.Attributes["hidden"] = "hidden";
            }

            if (tab.Content is not null)
            {
                panel.InnerHtml.AppendHtml(tab.Content);
            }

            panels.InnerHtml.AppendHtml(panel);
        }

        output.Content.SetHtmlContent(nav);
        output.Content.AppendHtml(panels);
    }

    private static int ResolveActiveIndex(MsTabsContext tabs)
    {
        if (tabs.Tabs.Count == 0)
        {
            return -1;
        }

        if (!string.IsNullOrWhiteSpace(tabs.ActiveKey))
        {
            var byKey = tabs.Tabs.FindIndex(tab => string.Equals(tab.Key, tabs.ActiveKey, StringComparison.OrdinalIgnoreCase));

            if (byKey >= 0)
            {
                return byKey;
            }
        }

        var requested = tabs.Tabs.FindIndex(tab => tab.RequestedActive);
        return requested >= 0 ? requested : 0;
    }
}
