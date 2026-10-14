using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.AspNetCore.WebUtilities;

namespace MatSplit.Web.Ui;

/// <summary>
/// The toolbar above a list (LIST-02, LIST-03): MatUi lays it out as the search field in the bar and Filter and
/// Sort as buttons that open a dialog. The fields written inside it are placed by the toolbar: the field of
/// type search is the search field, the field called sort goes to the Sort dialog, every other one to the
/// Filter dialog. All of them belong to the one GET form of the toolbar, so each dialog sends the whole state,
/// and a tool in effect shows as a chip with an X (LIST-04). When placed inside a ms-list it registers itself
/// as the toolbar slot, so the position is enforced by the list.
/// </summary>
[HtmlTargetElement("ms-toolbar")]
public sealed class MsToolbarTagHelper : MsTagHelperBase
{
    protected override string ControlName => "toolbar";

    /// <summary>Form target, defaults to the current url.</summary>
    public string? Action { get; set; }

    /// <summary>Http method, get for filters.</summary>
    public string Method { get; set; } = "get";

    /// <summary>Label of the button that applies the tools in a dialog.</summary>
    public string SubmitLabel { get; set; } = "Anwenden";

    /// <summary>When set a reset link pointing to this url is rendered in the dialogs.</summary>
    public string? ResetUrl { get; set; }

    /// <summary>Label of the reset link.</summary>
    public string ResetLabel { get; set; } = "Zurücksetzen";

    /// <summary>Kept for the pages that name it: the filter is sent with the button of its dialog, the search with Enter.</summary>
    public bool AutoSubmit { get; set; } = true;

    /// <summary>Kept for the pages that name it.</summary>
    public bool AutoSubmitText { get; set; }

    /// <summary>Adds a hidden field that resets the paging when filtering.</summary>
    public bool ResetPage { get; set; } = true;

    /// <summary>Name of the paging query parameter.</summary>
    public string PageParam { get; set; } = "page";

    /// <summary>Comma separated query keys that are carried over as hidden inputs.</summary>
    public string? Preserve { get; set; }

    /// <summary>Kept for the pages that name it.</summary>
    public bool HideSubmit { get; set; }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(output);

        var id = ResolveId();
        var formId = id + "-form";
        var tools = new MsToolbarContext(formId);
        context.Items[typeof(MsToolbarContext)] = tools;

        await output.GetChildContentAsync();

        var query = ViewContext.HttpContext.Request.Query;
        string Current(string name) => query.TryGetValue(name, out var values) ? values.ToString() : string.Empty;

        var search = tools.Fields.FirstOrDefault(f => f.Type == "search");
        var sort = tools.Fields.FirstOrDefault(f => string.Equals(f.Name, "sort", StringComparison.OrdinalIgnoreCase));
        var filters = tools.Fields.Where(f => f != search && f != sort).ToList();

        var filtersInEffect = filters.Count(f => !string.IsNullOrEmpty(Current(f.Name)));
        var sortInEffect = sort is not null
            && !string.IsNullOrEmpty(Current(sort.Name))
            && sort.Items is { Count: > 0 } sortItems
            && !string.Equals(sortItems[0].Value, Current(sort.Name), StringComparison.Ordinal);

        // No wrapper: the bar, the chips and the dialogs are children of mat-list, which orders them.
        var root = new HtmlContentBuilder();

        // ----- the bar
        var bar = new TagBuilder("div");
        bar.AddCssClass(MsHtml.Classes("mat-toolbar", CssClass));
        bar.Attributes["id"] = id;
        bar.Attributes["role"] = "toolbar";
        bar.Attributes["aria-label"] = "Werkzeuge der Liste";

        if (search is not null)
        {
            var wrap = new TagBuilder("div");
            wrap.AddCssClass("mat-input-icon mat-toolbar__search");
            wrap.InnerHtml.AppendHtml(MsHtml.Icon("search"));

            var input = new TagBuilder("input")
            {
                TagRenderMode = TagRenderMode.SelfClosing
            };
            input.AddCssClass("mat-input");
            input.Attributes["type"] = "search";
            input.Attributes["id"] = search.Id;
            input.Attributes["name"] = search.Name;
            input.Attributes["form"] = formId;
            input.Attributes["value"] = Current(search.Name);
            input.Attributes["placeholder"] = search.Placeholder ?? search.Label;
            input.Attributes["aria-label"] = search.Label;
            input.Attributes["autocomplete"] = "off";
            input.Attributes["enterkeyhint"] = "search";
            input.Attributes["data-shortcut"] = "/";
            wrap.InnerHtml.AppendHtml(input);
            bar.InnerHtml.AppendHtml(wrap);
        }

        var spacer = new TagBuilder("span");
        spacer.AddCssClass("mat-toolbar__spacer");
        bar.InnerHtml.AppendHtml(spacer);

        var end = new TagBuilder("div");
        end.AddCssClass("mat-toolbar__end");

        if (filters.Count > 0)
        {
            end.InnerHtml.AppendHtml(DialogButton(id + "-filter", "sliders", "Filter", filtersInEffect > 0, filtersInEffect > 0 ? filtersInEffect.ToString(System.Globalization.CultureInfo.InvariantCulture) : null, null));
        }

        if (sort is not null)
        {
            var sortText = sortInEffect ? sort.Items!.FirstOrDefault(i => i.Value == Current(sort.Name))?.Text : null;
            end.InnerHtml.AppendHtml(DialogButton(id + "-sort", "sort", "Sortieren", sortInEffect, null, sortText));
        }

        bar.InnerHtml.AppendHtml(end);
        root.AppendHtml(bar);

        // ----- the tools in effect, each removable (LIST-04)
        var chips = BuildChips(search, filters, sort, sortInEffect, Current);

        if (chips is not null)
        {
            root.AppendHtml(chips);
        }

        // ----- the one GET form every field belongs to
        var form = new TagBuilder("form");
        form.Attributes["id"] = formId;
        form.Attributes["method"] = string.IsNullOrWhiteSpace(Method) ? "get" : Method.ToLowerInvariant();
        form.Attributes["role"] = "search";
        form.Attributes["hidden"] = "hidden";

        if (!string.IsNullOrWhiteSpace(Action))
        {
            form.Attributes["action"] = Action!;
        }

        if (ResetPage)
        {
            form.InnerHtml.AppendHtml(Hidden(PageParam, "1"));
        }

        MsHtml.AppendPreservedQuery(form, ViewContext, Preserve);

        // Enter in the search field sends the form: a default button keeps the implicit submission working with several fields.
        var defaultButton = new TagBuilder("button");
        defaultButton.Attributes["type"] = "submit";
        defaultButton.Attributes["tabindex"] = "-1";
        defaultButton.AddCssClass("mat-sr-only");
        defaultButton.InnerHtml.Append(SubmitLabel);
        form.InnerHtml.AppendHtml(defaultButton);
        root.AppendHtml(form);

        // ----- the dialogs
        if (filters.Count > 0)
        {
            root.AppendHtml(Dialog(id + "-filter", formId, "sliders", "Filter", filters));
        }

        if (sort is not null)
        {
            root.AppendHtml(Dialog(id + "-sort", formId, "sort", "Sortieren", new[] { sort }));
        }

        MsHtml.CopyAttributes(output, bar);

        if (context.Items.TryGetValue(typeof(MsListContext), out var raw) && raw is MsListContext list)
        {
            list.Toolbar = root;
            output.SuppressOutput();
            return;
        }

        output.TagName = null;
        output.Content.SetHtmlContent(root);
    }

    private static TagBuilder Hidden(string name, string value)
    {
        var hidden = new TagBuilder("input")
        {
            TagRenderMode = TagRenderMode.SelfClosing
        };
        hidden.Attributes["type"] = "hidden";
        hidden.Attributes["name"] = name;
        hidden.Attributes["value"] = value;
        return hidden;
    }

    private static TagBuilder DialogButton(string dialogId, string icon, string label, bool active, string? count, string? value)
    {
        var button = new TagBuilder("button");
        button.AddCssClass(MsHtml.Classes("mat-btn mat-btn--ghost mat-toolbar__btn", active ? "is-active" : null));
        button.Attributes["type"] = "button";
        button.Attributes["data-dialog-open"] = dialogId;
        button.Attributes["aria-haspopup"] = "dialog";
        button.InnerHtml.AppendHtml(MsHtml.Icon(icon));

        var text = new TagBuilder("span");
        text.InnerHtml.Append(label);
        button.InnerHtml.AppendHtml(text);

        if (count is not null)
        {
            var badge = new TagBuilder("span");
            badge.AddCssClass("mat-badge");
            badge.InnerHtml.Append(count);
            button.InnerHtml.AppendHtml(badge);
        }

        if (!string.IsNullOrWhiteSpace(value))
        {
            var small = new TagBuilder("small");
            small.InnerHtml.Append(value);
            button.InnerHtml.AppendHtml(small);
        }

        return button;
    }

    private TagBuilder Dialog(string dialogId, string formId, string icon, string title, IReadOnlyList<MsToolbarField> fields)
    {
        var dialog = new TagBuilder("dialog");
        dialog.AddCssClass("mat-dialog mat-dialog--sm");
        dialog.Attributes["id"] = dialogId;
        dialog.Attributes["aria-labelledby"] = dialogId + "-title";

        var inner = new TagBuilder("div");
        inner.AddCssClass("mat-dialog__form");

        var head = new TagBuilder("div");
        head.AddCssClass("mat-dialog__head");
        head.InnerHtml.AppendHtml(MsHtml.Icon(icon));

        var heading = new TagBuilder("h2");
        heading.AddCssClass("mat-dialog__title");
        heading.Attributes["id"] = dialogId + "-title";
        heading.InnerHtml.Append(title);
        head.InnerHtml.AppendHtml(heading);

        var close = new TagBuilder("button");
        close.AddCssClass("mat-btn mat-btn--ghost mat-btn--icon mat-dialog__close");
        close.Attributes["type"] = "button";
        close.Attributes["data-dialog-close"] = "true";
        close.Attributes["aria-label"] = "Schließen";
        close.InnerHtml.AppendHtml(MsHtml.Icon("x"));
        head.InnerHtml.AppendHtml(close);
        inner.InnerHtml.AppendHtml(head);

        var body = new TagBuilder("div");
        body.AddCssClass("mat-dialog__body mat-form");

        foreach (var field in fields)
        {
            body.InnerHtml.AppendHtml(field.Html);
        }

        inner.InnerHtml.AppendHtml(body);

        var foot = new TagBuilder("div");
        foot.AddCssClass("mat-dialog__foot");

        var row = new TagBuilder("div");
        row.AddCssClass("mat-btn-row mat-btn-row--plain");

        var apply = new TagBuilder("button");
        apply.AddCssClass("mat-btn mat-btn--primary");
        apply.Attributes["type"] = "submit";
        apply.Attributes["form"] = formId;
        apply.InnerHtml.AppendHtml(MsHtml.Icon(icon));
        var applyText = new TagBuilder("span");
        applyText.InnerHtml.Append(SubmitLabel);
        apply.InnerHtml.AppendHtml(applyText);
        row.InnerHtml.AppendHtml(apply);

        var cancel = new TagBuilder("button");
        cancel.AddCssClass("mat-btn mat-btn--secondary");
        cancel.Attributes["type"] = "button";
        cancel.Attributes["data-dialog-close"] = "true";
        cancel.InnerHtml.Append("Abbrechen");
        row.InnerHtml.AppendHtml(cancel);

        if (!string.IsNullOrWhiteSpace(ResetUrl))
        {
            var spacer = new TagBuilder("span");
            spacer.AddCssClass("mat-btn-row__spacer");
            row.InnerHtml.AppendHtml(spacer);

            var reset = new TagBuilder("a");
            reset.AddCssClass("mat-btn mat-btn--ghost mat-btn--end");
            reset.Attributes["href"] = ResetUrl!;
            reset.InnerHtml.Append(ResetLabel);
            row.InnerHtml.AppendHtml(reset);
        }

        foot.InnerHtml.AppendHtml(row);
        inner.InnerHtml.AppendHtml(foot);
        dialog.InnerHtml.AppendHtml(inner);
        return dialog;
    }

    private TagBuilder? BuildChips(
        MsToolbarField? search,
        IReadOnlyList<MsToolbarField> filters,
        MsToolbarField? sort,
        bool sortInEffect,
        Func<string, string> current)
    {
        var chips = new TagBuilder("div");
        chips.AddCssClass("mat-chips");
        chips.Attributes["role"] = "group";
        chips.Attributes["aria-label"] = "Aktive Werkzeuge";
        var any = false;

        var inEffect = new List<MsToolbarField>(filters);

        if (search is not null)
        {
            inEffect.Add(search);
        }

        if (sort is not null && sortInEffect)
        {
            inEffect.Add(sort);
        }

        foreach (var field in inEffect)
        {
            var value = current(field.Name);

            if (string.IsNullOrEmpty(value))
            {
                continue;
            }

            var text = field.Items?.FirstOrDefault(item => item.Value == value)?.Text ?? value;
            var chip = new TagBuilder("span");
            chip.AddCssClass("mat-chip");
            chip.InnerHtml.Append(field.Label + ": " + text);

            var remove = new TagBuilder("a");
            remove.AddCssClass("mat-chip__x");
            remove.Attributes["href"] = RemoveUrl(field.Name);
            remove.Attributes["aria-label"] = field.Label + " entfernen";
            remove.InnerHtml.AppendHtml(MsHtml.Icon("x"));
            chip.InnerHtml.AppendHtml(remove);
            chips.InnerHtml.AppendHtml(chip);
            any = true;
        }

        return any ? chips : null;
    }

    /// <summary>The current address without one tool (and back on page one).</summary>
    private string RemoveUrl(string name)
    {
        var request = ViewContext.HttpContext.Request;
        var kept = request.Query
            .Where(pair => !string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(pair.Key, PageParam, StringComparison.OrdinalIgnoreCase))
            .ToDictionary(pair => pair.Key, pair => (string?)pair.Value.ToString());

        return QueryHelpers.AddQueryString(request.Path.Value ?? "/", kept);
    }
}
