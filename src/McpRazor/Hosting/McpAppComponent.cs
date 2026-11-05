using Microsoft.AspNetCore.Components;

namespace McpRazor;

/// <summary>
/// Marks a Razor component as the UI of an MCP App, served at <see cref="Uri"/>.
/// Use it from a <c>.razor</c> file with <c>@attribute [McpApp("ui://orders/review")]</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class McpAppAttribute(string uri) : Attribute
{
    /// <summary>The <c>ui://</c> URI of the UI resource. Tools reference it with <c>[McpAppUi(ResourceUri = ...)]</c>.</summary>
    public string Uri { get; } = uri;

    /// <summary>Resource name shown by hosts. Defaults to the component name.</summary>
    public string? Name { get; set; }

    /// <summary>Optional human-readable description of the UI.</summary>
    public string? Description { get; set; }

    /// <summary>Asks the host to draw a border around the app.</summary>
    public bool PrefersBorder { get; set; }
}

/// <summary>
/// Base class for the root component of an MCP App. <see cref="Data"/> receives the
/// <c>structuredContent</c> of the tool result, deserialized to <typeparamref name="TData"/>.
/// </summary>
/// <typeparam name="TData">The type the tool returns as structured content.</typeparam>
public abstract class McpAppComponent<TData> : ComponentBase
{
    /// <summary>The tool's <c>structuredContent</c>.</summary>
    [Parameter]
    public TData Data { get; set; } = default!;

    /// <summary>The MCP host showing this app: its context, and the requests the app can make to it.</summary>
    [Inject]
    protected IMcpAppHost Host { get; set; } = default!;
}
