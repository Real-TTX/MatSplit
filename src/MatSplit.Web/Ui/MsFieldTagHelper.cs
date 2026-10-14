using System.Globalization;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace MatSplit.Web.Ui;

/// <summary>
/// Label, input, hint and validation message as one MatUi field (<c>mat-field</c>). Bind it either with
/// for="Input.Name" (model binding, display name and the messages of the validation attributes come from
/// the model) or with name/value for filter fields. Supports progressive disclosure through
/// depends-on / depends-value (<c>data-show-if</c> of MatUi).
/// </summary>
[HtmlTargetElement("ms-field")]
public sealed class MsFieldTagHelper : MsTagHelperBase
{
    private readonly IHtmlGenerator _generator;
    private string? _toolbarFormId;

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

    /// <summary>
    /// The field is not required although its model property is (a non nullable value type is required for the model
    /// binder). For a field the server asks for only in some cases, such as the user a member is taken from.
    /// </summary>
    public bool Optional { get; set; }

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

        // In a toolbar the field belongs to the GET form of the bar and is placed by the toolbar.
        var toolbar = context.Items.TryGetValue(typeof(MsToolbarContext), out var rawToolbar) ? rawToolbar as MsToolbarContext : null;
        _toolbarFormId = toolbar?.FormId;

        output.TagName = null;

        if (string.Equals(type, "hidden", StringComparison.Ordinal))
        {
            output.Content.SetHtmlContent(BuildHidden(id, name));
            return;
        }

        var fieldName = For?.Name ?? name;
        var modelError = ModelError(fieldName);

        var wrapper = new TagBuilder("div");
        wrapper.Attributes["id"] = id + "-field";
        wrapper.AddCssClass(MsHtml.Classes(
            "mat-field",
            FullWidth ? "mat-field--full" : null,
            modelError is null ? null : "is-invalid",
            CssClass));

        if (!string.IsNullOrWhiteSpace(DependsOn))
        {
            // MatUi shows the field while the master field has the value and disables it while it is hidden.
            wrapper.Attributes["data-show-if"] = DependsOn!;
            wrapper.Attributes["data-show-value"] = DependsValue ?? "*";
            wrapper.Attributes["hidden"] = "hidden";
        }

        var isCheckbox = string.Equals(type, "checkbox", StringComparison.Ordinal);
        var isStatic = string.Equals(type, "static", StringComparison.Ordinal);
        var labelText = ResolveLabel();

        // A non nullable bool is "required" for the model binder, but a cleared
        // checkbox is a perfectly valid value - never mark it with an asterisk.
        var showRequiredMarker = Required || (!isCheckbox && !Optional && For?.Metadata.IsRequired == true);

        if (!isCheckbox && !string.IsNullOrWhiteSpace(labelText))
        {
            wrapper.InnerHtml.AppendHtml(BuildLabel(id, labelText!, showRequiredMarker));
        }

        var input = BuildInput(id, name, type, children, labelText, showRequiredMarker);
        var suffix = ResolveSuffix(type);

        if (string.IsNullOrWhiteSpace(Prefix) && string.IsNullOrWhiteSpace(suffix))
        {
            wrapper.InnerHtml.AppendHtml(input);
        }
        else
        {
            var group = new TagBuilder("div");
            group.AddCssClass("mat-input-group");

            if (!string.IsNullOrWhiteSpace(Prefix))
            {
                group.InnerHtml.AppendHtml(Addon(Prefix!));
            }

            group.InnerHtml.AppendHtml(input);

            if (!string.IsNullOrWhiteSpace(suffix))
            {
                group.InnerHtml.AppendHtml(Addon(suffix!));
            }

            wrapper.InnerHtml.AppendHtml(group);
        }

        if (Camera && string.Equals(type, "file", StringComparison.Ordinal))
        {
            wrapper.InnerHtml.AppendHtml(BuildCameraButton(id));
        }

        if (!string.IsNullOrWhiteSpace(Hint))
        {
            var hint = new TagBuilder("span");
            hint.AddCssClass("mat-hint");
            hint.Attributes["id"] = id + "-hint";
            hint.InnerHtml.Append(Hint!);
            wrapper.InnerHtml.AppendHtml(hint);
        }

        if (!NoValidation && !isStatic)
        {
            // The reserved line under the field: the server writes the model error here, MatUi the client side one.
            var error = new TagBuilder("span");
            error.AddCssClass("mat-error");
            error.Attributes["id"] = id + "-error";
            error.Attributes["aria-live"] = "polite";

            if (modelError is not null)
            {
                error.InnerHtml.Append(modelError);
            }

            wrapper.InnerHtml.AppendHtml(error);
        }

        MsHtml.CopyAttributes(output, wrapper);

        if (toolbar is not null)
        {
            toolbar.Fields.Add(new MsToolbarField(id, name, labelText ?? name, type, Placeholder, wrapper, Items?.ToList()));
            output.SuppressOutput();
            return;
        }

        output.Content.SetHtmlContent(wrapper);
    }

    private static TagBuilder Addon(string text)
    {
        var addon = new TagBuilder("span");
        addon.AddCssClass("mat-input-addon");
        addon.InnerHtml.Append(text);
        return addon;
    }

    private static TagBuilder RequiredMarker()
    {
        var marker = new TagBuilder("span");
        marker.AddCssClass("mat-req");
        marker.Attributes["aria-hidden"] = "true";
        marker.InnerHtml.Append("*");
        return marker;
    }

    /// <summary>The first message the model binder or the model validation left for the field, null when there is none.</summary>
    private string? ModelError(string fieldName)
    {
        if (!ViewContext.ViewData.ModelState.TryGetValue(fieldName, out var entry))
        {
            return null;
        }

        return entry.Errors
            .Select(error => error.ErrorMessage)
            .FirstOrDefault(message => !string.IsNullOrWhiteSpace(message));
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
        button.AddCssClass("mat-btn mat-btn--secondary mat-btn--sm ms-camera-button");
        button.Attributes["type"] = "button";
        button.Attributes["id"] = id + "-camera";
        button.Attributes["data-ms-camera"] = id;
        button.InnerHtml.AppendHtml(MsHtml.Icon("camera"));

        var label = new TagBuilder("span");
        label.InnerHtml.Append("Foto aufnehmen");
        button.InnerHtml.AppendHtml(label);
        return button;
    }

    private IHtmlContent BuildLabel(string id, string text, bool required)
    {
        var label = new TagBuilder("label");
        label.AddCssClass(LabelHidden ? "mat-label mat-sr-only" : "mat-label");
        label.Attributes["for"] = id;
        label.InnerHtml.Append(text);

        if (required)
        {
            label.InnerHtml.Append(" ");
            label.InnerHtml.AppendHtml(RequiredMarker());
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
                stat.InnerHtml.Append(Value ?? ModelText() ?? "–");
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
            return NativeValidation(BuildTextArea(name, attributes), Optional);
        }

        if (string.Equals(type, "select", StringComparison.Ordinal))
        {
            return NativeValidation(BuildSelect(name, children, attributes), Optional);
        }

        if (string.Equals(type, "file", StringComparison.Ordinal))
        {
            return BuildFile(name, attributes);
        }

        return NativeValidation(BuildTextBox(name, type, attributes), Optional);
    }

    private Dictionary<string, object> BuildAttributes(string id, string type)
    {
        var cssClass = type switch
        {
            "checkbox" => string.Empty,
            "select" => "mat-select",
            "textarea" => "mat-textarea",
            "money" => "mat-input",
            _ => "mat-input"
        };

        var attributes = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["id"] = id
        };

        if (!string.IsNullOrEmpty(cssClass))
        {
            attributes["class"] = cssClass;
        }

        if (!string.IsNullOrEmpty(_toolbarFormId))
        {
            attributes["form"] = _toolbarFormId;
        }

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

        var describedBy = new List<string>();

        if (!string.IsNullOrWhiteSpace(Hint))
        {
            describedBy.Add(id + "-hint");
        }

        if (!NoValidation)
        {
            describedBy.Add(id + "-error");
        }

        if (describedBy.Count > 0)
        {
            attributes["aria-describedby"] = string.Join(' ', describedBy);
        }

        if (string.Equals(type, "money", StringComparison.Ordinal))
        {
            // Text input with a decimal keypad; the value is kept invariant
            // (dot) by site.js so model binding and validation stay correct.
            attributes["inputmode"] = "decimal";
            attributes["autocomplete"] = "off";
            attributes["data-ms-money"] = "true";
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
        wrapper.AddCssClass("mat-check");
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
        caption.AddCssClass("mat-check__text");
        caption.InnerHtml.Append(labelText ?? string.Empty);

        if (required)
        {
            caption.InnerHtml.Append(" ");
            caption.InnerHtml.AppendHtml(RequiredMarker());
        }

        wrapper.InnerHtml.AppendHtml(caption);
        return wrapper;
    }

    /// <summary>
    /// MatUi checks the native constraints of a field (<c>required</c>, <c>maxlength</c>, <c>pattern</c> ...) and
    /// writes the message from <c>data-msg-*</c>. The model validation attributes of ASP.NET carry the same rules
    /// as <c>data-val-*</c> with the German message, so they are turned into the native form here: a rule the model
    /// has is checked in the browser, with the words of the model.
    /// </summary>
    private static IHtmlContent NativeValidation(IHtmlContent content, bool optional)
    {
        if (content is not TagBuilder tag)
        {
            return content;
        }

        string? Rule(string key) => tag.Attributes.TryGetValue("data-val-" + key, out var value) ? value : null;

        var required = optional ? null : Rule("required");

        if (required is not null)
        {
            tag.Attributes["required"] = "required";
            tag.Attributes["data-msg-required"] = required;
        }

        var length = Rule("length");

        if (length is not null)
        {
            if (tag.Attributes.TryGetValue("data-val-length-max", out var max))
            {
                tag.Attributes["maxlength"] = max;
            }

            if (tag.Attributes.TryGetValue("data-val-length-min", out var min))
            {
                tag.Attributes["minlength"] = min;
            }

            tag.Attributes["data-msg-range"] = length;
        }

        var maxLength = Rule("maxlength");

        if (maxLength is not null && tag.Attributes.TryGetValue("data-val-maxlength-max", out var maxLengthValue))
        {
            tag.Attributes["maxlength"] = maxLengthValue;
            tag.Attributes["data-msg-range"] = maxLength;
        }

        var range = Rule("range");

        if (range is not null)
        {
            if (tag.Attributes.TryGetValue("data-val-range-min", out var min) && !tag.Attributes.ContainsKey("min"))
            {
                tag.Attributes["min"] = min;
            }

            if (tag.Attributes.TryGetValue("data-val-range-max", out var max) && !tag.Attributes.ContainsKey("max"))
            {
                tag.Attributes["max"] = max;
            }

            tag.Attributes["data-msg-range"] = range;
        }

        var regex = Rule("regex");

        if (regex is not null)
        {
            if (tag.Attributes.TryGetValue("data-val-regex-pattern", out var pattern) && !tag.Attributes.ContainsKey("pattern"))
            {
                tag.Attributes["pattern"] = pattern;
            }

            tag.Attributes["data-msg-pattern"] = regex;
        }

        var typeMessage = Rule("email") ?? Rule("url") ?? Rule("phone");

        if (typeMessage is not null)
        {
            tag.Attributes["data-msg-type"] = typeMessage;
        }

        return tag;
    }

    /// <summary>
    /// Removes the framework's <c>data-val-required</c> from controls that can
    /// never be empty on the client: a checkbox posts "false" when unchecked and
    /// a hidden field always carries a server side value.
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
        // Money renders as text (not number) so a comma decimal separator can be
        // typed on any locale/keyboard; site.js normalises it to an invariant dot.
        "money" => "text",
        "number" => "number",
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
