using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using ModelContextProtocol;
using ModelContextProtocol.Extensions.Apps;

namespace McpRazor;

/// <summary>Configures the MCP Apps served from Razor components.</summary>
public sealed class McpRazorOptions
{
    private readonly Dictionary<string, McpAppRegistration> _apps = new(StringComparer.Ordinal);

    /// <summary>How long a view is kept in memory without events before it is released. Default: 30 minutes.</summary>
    public TimeSpan ViewIdleTimeout { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>Maximum number of live views. The least recently used one is released when it is reached. Default: 500.</summary>
    public int MaxViews { get; set; } = 500;

    /// <summary>
    /// The culture apps run with when the host does not send its locale, for example in the first view, which is
    /// rendered before the host shares it. Default: <see langword="null"/>, the server's culture.
    /// </summary>
    public CultureInfo? DefaultCulture { get; set; }

    /// <summary>
    /// When the first view of an app is rendered inside the tool result. Default:
    /// <see cref="McpAppPrerender.WhenHostSupportsApps"/>.
    /// </summary>
    public McpAppPrerender Prerender { get; set; } = McpAppPrerender.WhenHostSupportsApps;

    /// <summary>
    /// Sends exception messages to the app when a component fails. Keep it off in production: the
    /// exception is always logged on the server. Default: <see langword="false"/>.
    /// </summary>
    public bool DetailedErrors { get; set; }

    /// <summary>
    /// Options used to deserialize the tool's <c>structuredContent</c> into the component's <c>Data</c>.
    /// The default matches the options the MCP SDK uses to serialize tool results (camelCase, enums as strings).
    /// </summary>
    public JsonSerializerOptions SerializerOptions { get; set; } = new(McpJsonUtilities.DefaultOptions);

    /// <summary>The registered apps, by URI.</summary>
    public IReadOnlyCollection<McpAppRegistration> Apps => _apps.Values;

    /// <summary>Registers <typeparamref name="TComponent"/> as the UI served at <paramref name="uri"/>.</summary>
    public McpRazorOptions AddApp<TComponent>(string uri, Action<McpAppRegistration>? configure = null)
        where TComponent : IComponent =>
        AddApp(typeof(TComponent), uri, configure);

    /// <summary>Registers <paramref name="componentType"/> as the UI served at <paramref name="uri"/>.</summary>
    public McpRazorOptions AddApp(Type componentType, string uri, Action<McpAppRegistration>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(componentType);
        if (!typeof(IComponent).IsAssignableFrom(componentType))
        {
            throw new ArgumentException($"{componentType} is not a Razor component.", nameof(componentType));
        }

        if (!uri.StartsWith("ui://", StringComparison.Ordinal) || uri.Length <= "ui://".Length)
        {
            throw new ArgumentException($"MCP App URIs must use the ui:// scheme: '{uri}'.", nameof(uri));
        }

        if (_apps.ContainsKey(uri))
        {
            throw new InvalidOperationException($"An app is already registered at '{uri}'.");
        }

        var registration = new McpAppRegistration(uri, componentType);
        configure?.Invoke(registration);
        _apps.Add(uri, registration);
        return this;
    }

    /// <summary>Registers every component of <paramref name="assembly"/> marked with <see cref="McpAppAttribute"/>.</summary>
    public McpRazorOptions AddAppsFromAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        foreach (var type in assembly.GetTypes().OrderBy(t => t.FullName, StringComparer.Ordinal))
        {
            if (type.GetCustomAttribute<McpAppAttribute>() is not { } attribute)
            {
                continue;
            }

            AddApp(type, attribute.Uri, registration =>
            {
                registration.Name = attribute.Name ?? registration.Name;
                registration.Description = attribute.Description;
                registration.PrefersBorder = attribute.PrefersBorder;
            });
        }

        return this;
    }

    internal McpAppRegistration? Find(string uri) => _apps.GetValueOrDefault(uri);
}

/// <summary>An MCP App served from a Razor component.</summary>
public sealed class McpAppRegistration
{
    internal McpAppRegistration(string uri, Type componentType)
    {
        Uri = uri;
        ComponentType = componentType;
        Name = componentType.Name;
        DataType = FindDataType(componentType);
    }

    /// <summary>The <c>ui://</c> URI of the UI resource.</summary>
    public string Uri { get; }

    /// <summary>The root component.</summary>
    public Type ComponentType { get; }

    /// <summary>The type of the component's <c>Data</c> parameter, or <see langword="null"/> if it has none.</summary>
    public Type? DataType { get; }

    /// <summary>Resource name shown by hosts.</summary>
    public string Name { get; set; }

    /// <summary>Optional description of the UI.</summary>
    public string? Description { get; set; }

    /// <summary>Asks the host to draw a border around the app.</summary>
    public bool PrefersBorder { get; set; }

    /// <summary>
    /// External origins the app needs. Leave it empty unless the app loads images, fonts or scripts from
    /// outside: by default hosts block every external request.
    /// </summary>
    public McpUiResourceCsp? Csp { get; set; }

    /// <summary>CSS added to the app shell, after the default styles. Use it for app-wide styles.</summary>
    public string? Stylesheet { get; set; }

    private static Type? FindDataType(Type componentType)
    {
        for (var type = componentType; type is not null; type = type.BaseType)
        {
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(McpAppComponent<>))
            {
                return type.GetGenericArguments()[0];
            }
        }

        var property = componentType.GetProperty("Data", BindingFlags.Public | BindingFlags.Instance);
        return property?.GetCustomAttribute<ParameterAttribute>() is not null ? property.PropertyType : null;
    }
}

/// <summary>When McpRazor renders the first view of an app inside the tool result.</summary>
public enum McpAppPrerender
{
    /// <summary>
    /// Only for clients that declared MCP Apps support (<c>io.modelcontextprotocol/ui</c>) when they connected.
    /// Clients that only show text, such as command-line agents, get no view and use no server memory.
    /// When the server does not know the client's capabilities (stateless HTTP), it prerenders.
    /// </summary>
    WhenHostSupportsApps,

    /// <summary>For every call, also from clients that did not declare MCP Apps support.</summary>
    Always,

    /// <summary>Never. The app asks for its first view with the app-only tool <c>mcp_razor_render</c>.</summary>
    Never,
}
