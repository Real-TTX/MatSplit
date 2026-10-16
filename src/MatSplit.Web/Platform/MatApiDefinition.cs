// A copy of MatIdentity's src/MatIdentity.Web/Platform/MatApiDefinition.cs (only the namespace differs), until the shared package MatPlatform
// exists (MatDesign docs/PLATFORM.md, "Shared code"). Change it there first and copy it back, so the apps stay in step.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace MatSplit.Web.Platform;

// The types of the platform contract (MatDesign docs/PLATFORM.md, contract version 1). MatIdentity is the first app that implements it; these types are
// meant to move into the shared package MatPlatform once a second app uses them, so they know nothing about MatIdentity.

/// <summary>The answer of <c>GET /api</c>: what the app is and how a consumer draws it. The values are never in it.</summary>
public sealed record MatApiDefinition
{
    public const int ContractVersion = 1;

    public int Contract { get; init; } = ContractVersion;

    public required MatAppInfo App { get; init; }

    public required MatAuthInfo Auth { get; init; }

    public IReadOnlyList<MatPage> Pages { get; init; } = [];

    public IReadOnlyList<MatStateItem> State { get; init; } = [];

    public IReadOnlyList<MatField> Configure { get; init; } = [];

    public IReadOnlyList<MatAction> Actions { get; init; } = [];

    public IReadOnlyList<MatCapability> Provides { get; init; } = [];

    public IReadOnlyList<MatCapability> Consumes { get; init; } = [];

    /// <summary>The roles of the app, the most powerful first; MatIdentity offers them when an administrator gives the app to people (null: the app has none to offer).</summary>
    public IReadOnlyList<MatRole>? Roles { get; init; }

    /// <summary>The paths the app implements (for the list in <c>GET /api</c>).</summary>
    public IReadOnlyList<string> Endpoints { get; init; } = [];

    /// <summary>How the contract is written: camelCase, nulls left out, enums as their names in lower case.</summary>
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };
}

public sealed record MatAppInfo(string Id, string Name, string Description, string Version, string? Icon, string? Accent, string Home = "/");

public sealed record MatAuthInfo(bool ApiKey, string? Identity);

public sealed record MatPage(string Id, string Label, string Icon, IReadOnlyList<MatSection> Sections);

/// <summary>A part of a page: <c>tiles</c>, <c>form</c>, <c>actions</c> or <c>link</c>.</summary>
public sealed record MatSection(string Kind, IReadOnlyList<string> Items, string? Label = null, string? Url = null);

/// <summary>A tile: what a value is and how it is shown (<c>number</c>, <c>text</c>, <c>bool</c>, <c>time</c>, <c>meter</c>, <c>status</c>).</summary>
public sealed record MatStateItem(string Key, string Label, string Widget = "number")
{
    public string? Icon { get; init; }
    public string? Unit { get; init; }
    public int? Decimals { get; init; }
    public double? WarnAbove { get; init; }
    public double? ErrorAbove { get; init; }

    /// <summary>For a meter: a number or the key of another value.</summary>
    public JsonElement? Max { get; init; }
}

/// <summary>A setting: <c>text</c>, <c>number</c>, <c>bool</c>, <c>select</c>, <c>url</c>, <c>email</c> or <c>secret</c>.</summary>
public sealed record MatField(string Key, string Label, string Type)
{
    public bool? Required { get; init; }
    public IReadOnlyList<MatOption>? Options { get; init; }
    public double? Min { get; init; }
    public double? Max { get; init; }
    public string? Pattern { get; init; }
    public string? Placeholder { get; init; }
    public string? Hint { get; init; }
    public string? Group { get; init; }
    public IReadOnlyDictionary<string, JsonElement>? ShowIf { get; init; }

    /// <summary>The value takes effect after a restart.</summary>
    public bool? Restart { get; init; }
}

public sealed record MatOption(string Value, string Label);

/// <summary>A button: <c>link</c> (opens <see cref="Url"/>) or <c>post</c> (<c>POST /api/actions/{id}</c>).</summary>
public sealed record MatAction(string Id, string Label, string Icon, string Kind)
{
    public string? Url { get; init; }
    public string? Confirm { get; init; }
    public bool? Danger { get; init; }
}

public sealed record MatCapability(string Capability, int Version, bool? Optional = null);

/// <summary>A role of the app: the value of the claim <c>role</c> and its name for people.</summary>
public sealed record MatRole(string Key, string Label);

/// <summary>A value of <c>GET /api/state</c>. The value is always written, also when it is null (a time that has not happened yet).</summary>
public sealed record MatStateValue([property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] object? Value, string? Level = null, string? Text = null);

/// <summary>The answer of <c>GET /api/health</c>.</summary>
public sealed record MatHealth(string Status, string App, string Version, DateTimeOffset Time, IReadOnlyList<MatHealthCheck> Checks);

public sealed record MatHealthCheck(string Name, string Status, string? Message = null);

/// <summary>The answer of <c>POST /api/actions/{id}</c>.</summary>
public sealed record MatActionResult(bool Ok, string Message);
