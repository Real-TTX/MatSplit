using System.Globalization;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace MatSplit.Web.Ui;

/// <summary>
/// Label, input, hint and validation message as one control. Bind it either with
/// for="Input.Name" (model binding, display name and client validation come from
/// the model) or with name/value for filter fields. Supports progressive
/// disclosure through depends-on / depends-value.
/// </summary>
[HtmlTargetElement("ms-field")]
public sealed class MsFieldTagHelper : MsTagHelperBase
{
    private readonly IHtmlGenerator _generator;

    public MsFieldTagHelper(IHtmlGenerator generator) => _generator = generator;

    protected override string ControlName => "field";

    /// <summary>Model expression, e.g. for="Input.Description".</summary>
    [HtmlAttributeName("for")]
    public ModelExpression? For { get; set; }

    /// <summary>Explicit form field name, required when for is not used.</summary>
    public string? Name { get; set; }

    /// <summary>
    /// text, textarea, select, checkbox, file, money, number, date, datetime,
    /// time, email, password, tel, url, search, color, hidden or static.
    /// </summary>
    public string Type { get; set; } = "text";

    /// <summary>Label text, falls back to the display name of the model property.</summary>
    public string? Label { get; set; }

    /// <summary>Renders the label for screen readers only.</summary>
    public bool LabelHidden { get; set; }

    /// <summary>Explicit value, overrides the model value.</summary>
    public string? Value { get; set; }

    /// <summary>Help text below the input.</summary>
    public string? Hint { get; set; }

    public string? Placeholder { get; set; }

    public bool Required { get; set; }

    [HtmlAttributeName("readonly")]
    public bool ReadOnly { get; set; }

    public bool Disabled { get; set; }

    public bool Multiple { get; set; }

    public bool Autofocus { get; set; }

    public string? Autocomplete { get; set; }

    [HtmlAttributeName("input-mode")]
    public string? InputMode { get; set; }

    public string? Pattern { get; set; }

    public string? Min { get; set; }

    public string? Max { get; set; }

    public string? Step { get; set; }

    [HtmlAttributeName("max-length")]
    public int? MaxLength { get; set; }

    public int Rows { get; set; } = 4;

    /// <summary>Options for type="select".</summary>
    public IEnumerable<SelectListItem>? Items { get; set; }

    /// <summary>Placeholder option for type="select".</summary>
    [HtmlAttributeName("option-label")]
    public string? OptionLabel { get; set; }

    /// <summary>Accept filter for type="file", e.g. image/*.</summary>
    public string? Accept { get; set; }

    /// <summary>Capture mode for type="file", e.g. environment for the rear camera.</summary>
    public string? Capture { get; set; }

    /// <summary>Adds a camera button next to a file input (getUserMedia fallback).</summary>
    public bool Camera { get; set; }

    /// <summary>Checked state for type="checkbox" without model binding.</summary>
    [HtmlAttributeName("checked")]
    public bool IsChecked { get; set; }

    /// <summary>Currency symbol used as suffix for type="money".</summary>
    public string Currency { get; set; } = "EUR";

    /// <summary>Static text or prefix shown in front of the input.</summary>
    public string? Prefix { get; set; }

    /// <summary>Unit shown behind the input.</summary>
    public string? Suffix { get; set; }

    /// <summary>Field grid: makes the field span all columns.</summary>
    [HtmlAttributeName("full-width")]
    public bool FullWidth { get; set; }

    /// <summary>Name of the field this field depends on (progressive disclosure).</summary>
    [HtmlAttributeName("depends-on")]
    public string? DependsOn { get; set; }

    /// <summary>
    /// Value (or comma separated values) the master field must have. Use "true"
    /// for checkboxes and "*" for any non empty value.
    /// </summary>
    [HtmlAttributeName("depends-value")]
    public string? DependsValue { get; set; }

    /// <summary>Suppresses the validation message element.</summary>
    [HtmlAttributeName("no-validation")]
    public bool NoValidation { get; set; }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(output);

        var id = ResolveId();
        var type = (Type ?? "text").Trim().ToLowerInvariant();
        var name = ResolveName(id);
        var children = await output.GetChildContentAsync();

        output.TagName = null;

        if (string.Equals(type, "hidden", StringComparison.Ordinal))
        {
            output.Content.SetHtmlContent(BuildHidden(id, name));
            return;
        }

        var wrapper = new TagBuilder("div");
        wrapper.Attributes["id"] = id + "-field";
        wrapper.AddCssClass(MsHtml.Classes(
            "ms-field",
            "ms-field--" + type,
            FullWidth ? "ms-field--full" : null,
            string.IsNullOrWhiteSpace(DependsOn) ? null : "ms-field--conditional",
            CssClass));

        if (!string.IsNullOrWhiteSpace(DependsOn))
        {
            wrapper.Attributes["data-depends-on"] = DependsOn!;
            wrapper.Attributes["data-depends-value"] = DependsValue ?? "*";
            wrapper.Attributes["hidden"] = "hidden";
        }

        var isCheckbox = string.Equals(type, "checkbox", StringComparison.Ordinal);
        var labelText = ResolveLabel();

        // A non nullable bool is "required" for the model binder, but a cleared
        // checkbox is a perfectly valid value - never mark it with an asterisk.
        var showRequiredMarker = Required || (!isCheckbox && For?.Metadata.IsRequired == true);

        if (!isCheckbox && !string.IsNullOrWhiteSpace(labelText))
        {
            wrapper.InnerHtml.AppendHtml(BuildLabel(id, labelText!, showRequiredMarker));
        }

        var control = new TagBuilder("div");
        control.AddCssClass("ms-field__control");

        if (!string.IsNullOrWhiteSpace(Prefix))
        {
            var prefix = new TagBuilder("span");
            prefix.AddCssClass("ms-field__prefix");
            prefix.InnerHtml.Append(Prefix!);
            control.InnerHtml.AppendHtml(prefix);
        }

        control.InnerHtml.AppendHtml(BuildInput(id, name, type, children, labelText, showRequiredMarker));

        var suffix = ResolveSuffix(type);

        if (!string.IsNullOrWhiteSpace(suffix))
        {
            var suffixTag = new TagBuilder("span");
            suffixTag.AddCssClass("ms-field__suffix");
            suffixTag.InnerHtml.Append(suffix!);
            control.InnerHtml.AppendHtml(suffixTag);
        }

        if (Camera && string.Equals(type, "file", StringComparison.Ordinal))
        {
            control.InnerHtml.AppendHtml(BuildCameraButton(id));
        }

        wrapper.InnerHtml.AppendHtml(control);

        if (!string.IsNullOrWhiteSpace(Hint))
        {
            var hint = new TagBuilder("p");
            hint.AddCssClass("ms-field__hint");
            hint.Attributes["id"] = id + "-hint";
            hint.InnerHtml.Append(Hint!);
            wrapper.InnerHtml.AppendHtml(hint);
        }

        if (!NoValidation && !string.Equals(type, "static", StringComparison.Ordinal))
        {
            var message = _generator.GenerateValidationMessage(
                ViewContext,
                For?.ModelExplorer,
                For?.Name ?? name,
                message: null,
                tag: "span",
                htmlAttributes: new { @class = "ms-field__error", id = id + "-error" });

            if (message is not null)
            {
                wrapper.InnerHtml.AppendHtml(message);
            }
        }

        MsHtml.CopyAttributes(output, wrapper);
        output.Content.SetHtmlContent(wrapper);
    }

    private string ResolveName(string id)
    {
        if (!string.IsNullOrWhiteSpace(Name))
        {
            return Name!;
        }

        return For is not null ? For.Name : id;
    }

    private string? ResolveLabel()
    {
        if (Label is not null)
        {
            return Label;
        }

        return For is null ? null : For.Metadata.DisplayName ?? For.Metadata.PropertyName ?? For.Name;
    }

    private string? ResolveSuffix(string type)
    {
        if (!string.IsNullOrWhiteSpace(Suffix))
        {
            return Suffix;
        }

        return string.Equals(type, "money", StringComparison.Ordinal)
            ? MsHtml.CurrencySymbol(Currency)
            : null;
    }

    private IHtmlContent BuildCameraButton(string id)
    {
        var button = new TagBuilder("button");
        button.AddCssClass("ms-btn ms-btn--ghost ms-btn--sm ms-field__camera");
        button.Attributes["type"] = "button";
        button.Attributes["id"] = id + "-camera";
        button.Attributes["data-ms-camera"] = id;
        button.InnerHtml.AppendHtml(MsHtml.Icon("camera", 16));

        var label = new TagBuilder("span");
        label.InnerHtml.Append("Foto aufnehmen");
        button.InnerHtml.AppendHtml(label);
        return button;
    }

    private IHtmlContent BuildLabel(string id, string text, bool required)
    {
        var label = new TagBuilder("label");
        label.AddCssClass(LabelHidden ? "ms-field__label ms-visually-hidden" : "ms-field__label");
        label.Attributes["for"] = id;

        var span = new TagBuilder("span");
        span.InnerHtml.Append(text);
        label.InnerHtml.AppendHtml(span);

        if (required)
        {
            var marker = new TagBuilder("abbr");
            marker.AddCssClass("ms-field__req");
            marker.Attributes["title"] = "Pflichtfeld";
            marker.InnerHtml.Append("*");
            label.InnerHtml.AppendHtml(marker);
        }

        return label;
    }

    private IHtmlContent BuildHidden(string id, string name)
    {
        if (For is not null)
        {
            var bound = _generator.GenerateHidden(
                ViewContext,
                For.ModelExplorer,
                For.Name,
                Value,
                useViewData: Value is null,
                htmlAttributes: new { id });

            // A hidden field carries its value from the server; client side
            // validation attributes on it are pure noise.
            DropRequiredValidation(bound);
            return bound;
        }

        var hidden = new TagBuilder("input")
        {
            TagRenderMode = TagRenderMode.SelfClosing
        };
        hidden.Attributes["type"] = "hidden";
        hidden.Attributes["id"] = id;
        hidden.Attributes["name"] = name;
        hidden.Attributes["value"] = Value ?? string.Empty;
        return hidden;
    }

    private IHtmlContent BuildInput(string id, string name, string type, TagHelperContent children, string? labelText, bool required)
    {
        if (string.Equals(type, "static", StringComparison.Ordinal))
        {
            var stat = new TagBuilder("p");
            stat.AddCssClass("ms-static");
            stat.Attributes["id"] = id;

            if (MsHtml.HasContent(children))
            {
                stat.InnerHtml.AppendHtml(children);
            }
            else
            {
                stat.InnerHtml.Append(Value ?? ModelText() ?? "\u2013");
            }

            return stat;
        }

        var attributes = BuildAttributes(id, type);

        if (string.Equals(type, "checkbox", StringComparison.Ordinal))
        {
            return BuildCheckbox(id, name, labelText, required, attributes);
        }

        if (string.Equals(type, "textarea", StringComparison.Ordinal))
        {
            return BuildTextArea(name, attributes);
        }

        if (string.Equals(type, "select", StringComparison.Ordinal))
        {
            return BuildSelect(name, children, attributes);
        }

        if (string.Equals(type, "file", StringComparison.Ordinal))
        {
            return BuildFile(name, attributes);
        }

        return BuildTextBox(name, type, attributes);
    }

    private Dictionary<string, object> BuildAttributes(string id, string type)
    {
        var cssClass = type switch
        {
            "checkbox" => "ms-checkbox",
            "select" => "ms-select",
            "textarea" => "ms-input ms-textarea",
            "money" => "ms-input ms-input--money",
            "file" => "ms-input ms-input--file",
            _ => "ms-input"
        };

        var attributes = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["id"] = id,
            ["class"] = cssClass
        };

        if (!string.IsNullOrWhiteSpace(Placeholder))
        {
            attributes["placeholder"] = Placeholder!;
        }

        if (Required)
        {
            attributes["required"] = "required";
        }

        if (ReadOnly)
        {
            attributes["readonly"] = "readonly";
        }

        if (Disabled)
        {
            attributes["disabled"] = "disabled";
        }

        if (Autofocus)
        {
            attributes["autofocus"] = "autofocus";
        }

        if (!string.IsNullOrWhiteSpace(Autocomplete))
        {
            attributes["autocomplete"] = Autocomplete!;
        }

        if (!string.IsNullOrWhiteSpace(Pattern))
        {
            attributes["pattern"] = Pattern!;
        }

        if (!string.IsNullOrWhiteSpace(Min))
        {
            attributes["min"] = Min!;
        }

        if (!string.IsNullOrWhiteSpace(Max))
        {
            attributes["max"] = Max!;
        }

        if (MaxLength.HasValue)
        {
            attributes["maxlength"] = MaxLength.Value.ToString(CultureInfo.InvariantCulture);
        }

        if (!string.IsNullOrWhiteSpace(Hint))
        {
            attributes["aria-describedby"] = id + "-hint";
        }

        if (string.Equals(type, "money", StringComparison.Ordinal))
        {
            attributes["step"] = string.IsNullOrWhiteSpace(Step) ? "0.01" : Step!;
            attributes["inputmode"] = "decimal";
        }
        else if (!string.IsNullOrWhiteSpace(Step))
        {
            attributes["step"] = Step!;
        }

        if (!string.IsNullOrWhiteSpace(InputMode))
        {
            attributes["inputmode"] = InputMode!;
        }

        if (Multiple)
        {
            attributes["multiple"] = "multiple";
        }

        return attributes;
    }

    private IHtmlContent BuildTextBox(string name, string type, Dictionary<string, object> attributes)
    {
        var htmlType = HtmlInputType(type);
        attributes["type"] = htmlType;

        if (For is not null)
        {
            if (string.Equals(htmlType, "password", StringComparison.Ordinal))
            {
                return _generator.GeneratePassword(ViewContext, For.ModelExplorer, For.Name, null, attributes);
            }

            // Explicit value wins, then the invariant formatted model value for
            // dates and decimals, finally the raw model value. The generator
            // still prefers a rejected ModelState value on a re-post.
            var modelValue = Value ?? ModelValueForInput(type) ?? For.Model;

            return _generator.GenerateTextBox(
                ViewContext,
                For.ModelExplorer,
                For.Name,
                modelValue,
                format: null,
                htmlAttributes: attributes);
        }

        var input = new TagBuilder("input")
        {
            TagRenderMode = TagRenderMode.SelfClosing
        };
        Apply(input, attributes);
        input.Attributes["name"] = name;

        if (!string.Equals(htmlType, "password", StringComparison.Ordinal))
        {
            input.Attributes["value"] = Value ?? string.Empty;
        }

        return input;
    }

    private IHtmlContent BuildTextArea(string name, Dictionary<string, object> attributes)
    {
        if (For is not null)
        {
            return _generator.GenerateTextArea(ViewContext, For.ModelExplorer, For.Name, Rows, 0, attributes);
        }

        var textarea = new TagBuilder("textarea");
        Apply(textarea, attributes);
        textarea.Attributes["name"] = name;
        textarea.Attributes["rows"] = Rows.ToString(CultureInfo.InvariantCulture);
        textarea.InnerHtml.Append(Value ?? string.Empty);
        return textarea;
    }

    private IHtmlContent BuildSelect(string name, TagHelperContent children, Dictionary<string, object> attributes)
    {
        if (For is not null)
        {
            var select = _generator.GenerateSelect(
                ViewContext,
                For.ModelExplorer,
                OptionLabel,
                For.Name,
                Items ?? [],
                Multiple,
                attributes);

            if (Items is null && MsHtml.HasContent(children))
            {
                select.InnerHtml.AppendHtml(children);
            }

            return select;
        }

        var manual = new TagBuilder("select");
        Apply(manual, attributes);
        manual.Attributes["name"] = name;

        if (!string.IsNullOrWhiteSpace(OptionLabel))
        {
            var empty = new TagBuilder("option");
            empty.Attributes["value"] = string.Empty;
            empty.InnerHtml.Append(OptionLabel!);
            manual.InnerHtml.AppendHtml(empty);
        }

        if (Items is not null)
        {
            foreach (var item in Items)
            {
                manual.InnerHtml.AppendHtml(BuildOption(item));
            }

            return manual;
        }

        if (MsHtml.HasContent(children))
        {
            manual.InnerHtml.AppendHtml(children);
        }

        return manual;
    }

    private TagBuilder BuildOption(SelectListItem item)
    {
        var option = new TagBuilder("option");
        option.Attributes["value"] = item.Value ?? item.Text ?? string.Empty;

        if (item.Selected || (Value is not null && string.Equals(Value, item.Value, StringComparison.Ordinal)))
        {
            option.Attributes["selected"] = "selected";
        }

        if (item.Disabled)
        {
            option.Attributes["disabled"] = "disabled";
        }

        option.InnerHtml.Append(item.Text ?? string.Empty);
        return option;
    }

    private IHtmlContent BuildFile(string name, Dictionary<string, object> attributes)
    {
        attributes["type"] = "file";

        if (!string.IsNullOrWhiteSpace(Accept))
        {
            attributes["accept"] = Accept!;
        }

        if (!string.IsNullOrWhiteSpace(Capture))
        {
            attributes["capture"] = Capture!;
        }

        var input = new TagBuilder("input")
        {
            TagRenderMode = TagRenderMode.SelfClosing
        };
        Apply(input, attributes);
        input.Attributes["name"] = For?.Name ?? name;
        return input;
    }

    private IHtmlContent BuildCheckbox(string id, string name, string? labelText, bool required, Dictionary<string, object> attributes)
    {
        var wrapper = new TagBuilder("label");
        wrapper.AddCssClass("ms-check");
        wrapper.Attributes["for"] = id;

        if (For is not null)
        {
            var box = _generator.GenerateCheckBox(ViewContext, For.ModelExplorer, For.Name, null, attributes);
            var hidden = _generator.GenerateHiddenForCheckbox(ViewContext, For.ModelExplorer, For.Name);

            DropRequiredValidation(box);
            DropRequiredValidation(hidden);

            wrapper.InnerHtml.AppendHtml(box);
            wrapper.InnerHtml.AppendHtml(hidden);
        }
        else
        {
            var input = new TagBuilder("input")
            {
                TagRenderMode = TagRenderMode.SelfClosing
            };
            Apply(input, attributes);
            input.Attributes["type"] = "checkbox";
            input.Attributes["name"] = name;
            input.Attributes["value"] = "true";

            if (IsChecked || string.Equals(Value, "true", StringComparison.OrdinalIgnoreCase))
            {
                input.Attributes["checked"] = "checked";
            }

            wrapper.InnerHtml.AppendHtml(input);

            var fallback = new TagBuilder("input")
            {
                TagRenderMode = TagRenderMode.SelfClosing
            };
            fallback.Attributes["type"] = "hidden";
            fallback.Attributes["name"] = name;
            fallback.Attributes["value"] = "false";
            wrapper.InnerHtml.AppendHtml(fallback);
        }

        var caption = new TagBuilder("span");
        caption.AddCssClass("ms-check__label");
        caption.InnerHtml.Append(labelText ?? string.Empty);

        if (required)
        {
            var marker = new TagBuilder("abbr");
            marker.AddCssClass("ms-field__req");
            marker.Attributes["title"] = "Pflichtfeld";
            marker.InnerHtml.Append("*");
            caption.InnerHtml.AppendHtml(marker);
        }

        wrapper.InnerHtml.AppendHtml(caption);
        return wrapper;
    }

    /// <summary>
    /// Removes the framework's <c>data-val-required</c> from controls that can
    /// never be empty on the client: a checkbox posts "false" when unchecked and
    /// a hidden field always carries a server side value. site.js never
    /// evaluates the rule, it would only leave an english message in the markup.
    /// </summary>
    private static void DropRequiredValidation(IHtmlContent content)
    {
        if (content is not TagBuilder tag)
        {
            return;
        }

        tag.Attributes.Remove("data-val-required");

        var hasRule = tag.Attributes.Keys.Any(
            key => key.StartsWith("data-val-", StringComparison.Ordinal));

        if (!hasRule)
        {
            tag.Attributes.Remove("data-val");
        }
    }

    private string? ModelText()
    {
        return For?.Model switch
        {
            null => null,
            DateTime date => date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture),
            DateOnly date => date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture),
            bool flag => flag ? "Ja" : "Nein",
            var model => model.ToString()
        };
    }

    private string? ModelValueForInput(string type)
    {
        var model = For?.Model;

        if (model is null)
        {
            return null;
        }

        var isTime = string.Equals(type, "time", StringComparison.Ordinal);
        var isDateTime = type is "datetime" or "datetime-local";

        // Money always keeps two decimals, other numbers stay as short as possible.
        // Number inputs need the invariant decimal point regardless of the culture.
        var numberFormat = string.Equals(type, "money", StringComparison.Ordinal) ? "0.00" : "0.##";

        return model switch
        {
            DateTime date when isTime => date.ToString("HH:mm", CultureInfo.InvariantCulture),
            DateTime date when isDateTime => date.ToString("yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture),
            DateTime date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            TimeOnly time => time.ToString("HH:mm", CultureInfo.InvariantCulture),
            decimal number => number.ToString(numberFormat, CultureInfo.InvariantCulture),
            double number => number.ToString(numberFormat, CultureInfo.InvariantCulture),
            float number => number.ToString(numberFormat, CultureInfo.InvariantCulture),
            _ => null
        };
    }

    private static string HtmlInputType(string type) => type switch
    {
        "money" or "number" => "number",
        "datetime" or "datetime-local" => "datetime-local",
        "date" => "date",
        "time" => "time",
        "email" => "email",
        "password" => "password",
        "tel" => "tel",
        "url" => "url",
        "search" => "search",
        "color" => "color",
        "month" => "month",
        _ => "text"
    };

    private static void Apply(TagBuilder tag, Dictionary<string, object> attributes)
    {
        foreach (var pair in attributes)
        {
            tag.Attributes[pair.Key] = pair.Value?.ToString() ?? string.Empty;
        }
    }
}
