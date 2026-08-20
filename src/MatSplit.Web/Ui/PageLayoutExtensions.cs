using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace MatSplit.Web.Ui;

/// <summary>
/// Convenience helpers so page models can fill the layout slots without
/// remembering the ViewData keys.
/// </summary>
public static class PageLayoutExtensions
{
    /// <summary>Sets title, optional subtitle and optional header icon.</summary>
    public static void SetTitle(this PageModel page, string title, string? subtitle = null, string? icon = null)
    {
        ArgumentNullException.ThrowIfNull(page);

        page.ViewData[LayoutKeys.Title] = title;

        if (subtitle is not null)
        {
            page.ViewData[LayoutKeys.Subtitle] = subtitle;
        }

        if (icon is not null)
        {
            page.ViewData[LayoutKeys.TitleIcon] = icon;
        }
    }

    /// <summary>Sets the breadcrumb shown below the header.</summary>
    public static void SetBreadcrumb(this PageModel page, params BreadcrumbItem[] items)
    {
        ArgumentNullException.ThrowIfNull(page);
        page.ViewData[LayoutKeys.Breadcrumb] = items;
    }

    /// <summary>Fills the group list of the left menu and marks the active group.</summary>
    public static void SetMenuGroups(this PageModel page, IEnumerable<MenuGroupEntry> groups, long? activeGroupId = null)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(groups);

        page.ViewData[LayoutKeys.MenuGroups] = groups as IReadOnlyList<MenuGroupEntry> ?? groups.ToList();

        if (activeGroupId.HasValue)
        {
            page.ViewData[LayoutKeys.ActiveGroupId] = activeGroupId.Value;
        }
    }

    /// <summary>Success message that survives a redirect.</summary>
    public static void Flash(this PageModel page, string message)
    {
        ArgumentNullException.ThrowIfNull(page);
        page.TempData[LayoutKeys.Flash] = message;
    }

    /// <summary>Error message that survives a redirect.</summary>
    public static void FlashError(this PageModel page, string message)
    {
        ArgumentNullException.ThrowIfNull(page);
        page.TempData[LayoutKeys.FlashError] = message;
    }

    /// <summary>
    /// Reads the menu groups from ViewData. Accepts the strongly typed list and
    /// also any enumerable of objects exposing Id/Name properties so pages may
    /// pass their own view models.
    /// </summary>
    public static IReadOnlyList<MenuGroupEntry> ReadMenuGroups(this ViewDataDictionary viewData)
    {
        ArgumentNullException.ThrowIfNull(viewData);

        var raw = viewData[LayoutKeys.MenuGroups];

        if (raw is IReadOnlyList<MenuGroupEntry> typedList)
        {
            return typedList;
        }

        if (raw is IEnumerable<MenuGroupEntry> typed)
        {
            return typed.ToList();
        }

        if (raw is string || raw is not System.Collections.IEnumerable loose)
        {
            return [];
        }

        var result = new List<MenuGroupEntry>();

        foreach (var item in loose)
        {
            var entry = Convert(item);
            if (entry is not null)
            {
                result.Add(entry);
            }
        }

        return result;
    }

    /// <summary>Reads the breadcrumb from ViewData.</summary>
    public static IReadOnlyList<BreadcrumbItem> ReadBreadcrumb(this ViewDataDictionary viewData)
    {
        ArgumentNullException.ThrowIfNull(viewData);

        return viewData[LayoutKeys.Breadcrumb] switch
        {
            IReadOnlyList<BreadcrumbItem> list => list,
            IEnumerable<BreadcrumbItem> items => items.ToList(),
            _ => []
        };
    }

    /// <summary>Reads the active group id from ViewData.</summary>
    public static long? ReadActiveGroupId(this ViewDataDictionary viewData)
    {
        ArgumentNullException.ThrowIfNull(viewData);

        return viewData[LayoutKeys.ActiveGroupId] switch
        {
            long value => value,
            int value => value,
            string text when long.TryParse(text, out var parsed) => parsed,
            _ => null
        };
    }

    /// <summary>Reads a string from ViewData, null when missing or empty.</summary>
    public static string? ReadString(this ViewDataDictionary viewData, string key)
    {
        ArgumentNullException.ThrowIfNull(viewData);

        var text = viewData[key] as string;
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    /// <summary>Reads a bool from ViewData, false when missing.</summary>
    public static bool ReadFlag(this ViewDataDictionary viewData, string key)
    {
        ArgumentNullException.ThrowIfNull(viewData);

        return viewData[key] switch
        {
            bool value => value,
            string text => bool.TryParse(text, out var parsed) && parsed,
            _ => false
        };
    }

    private static MenuGroupEntry? Convert(object? item)
    {
        if (item is null)
        {
            return null;
        }

        if (item is MenuGroupEntry entry)
        {
            return entry;
        }

        var type = item.GetType();
        var idProperty = type.GetProperty("Id") ?? type.GetProperty("GroupId");
        var nameProperty = type.GetProperty("Name") ?? type.GetProperty("DisplayName") ?? type.GetProperty("Title");

        if (idProperty is null || nameProperty is null)
        {
            return null;
        }

        var idValue = idProperty.GetValue(item);
        var nameValue = nameProperty.GetValue(item);

        if (idValue is null || nameValue is null)
        {
            return null;
        }

        var id = idValue switch
        {
            long value => value,
            int value => value,
            _ => long.TryParse(idValue.ToString(), out var parsed) ? parsed : 0L
        };

        if (id == 0)
        {
            return null;
        }

        var isGroupAdmin = type.GetProperty("IsGroupAdmin")?.GetValue(item) is true;
        return new MenuGroupEntry(id, nameValue.ToString() ?? string.Empty, isGroupAdmin);
    }
}
