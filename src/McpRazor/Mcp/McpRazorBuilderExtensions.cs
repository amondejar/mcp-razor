using System.Text.Json;
using System.Text.Json.Nodes;
using McpRazor;
using McpRazor.Hosting;
using McpRazor.Mcp;
using McpRazor.Runtime;
using McpRazor.Views;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ModelContextProtocol.Extensions.Apps;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Registers Razor components as MCP Apps on an MCP server.</summary>
public static class McpRazorBuilderExtensions
{
    /// <summary>
    /// Serves Razor components as MCP Apps. Each registered component gets a <c>ui://</c> resource that tools can
    /// reference with <c>[McpAppUi(ResourceUri = ...)]</c>; McpRazor adds the app-only tools that render it and run
    /// its events, and enables MCP Apps support on the server.
    /// </summary>
    /// <example>
    /// <code>
    /// builder.Services.AddMcpServer()
    ///     .WithHttpTransport()
    ///     .WithTools&lt;LoanTools&gt;()
    ///     .WithRazorApps(apps => apps.AddAppsFromAssembly(typeof(Program).Assembly));
    /// </code>
    /// </example>
    public static IMcpServerBuilder WithRazorApps(this IMcpServerBuilder builder, Action<McpRazorOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new McpRazorOptions();
        configure(options);
        if (options.Apps.Count == 0)
        {
            throw new InvalidOperationException("WithRazorApps was called without registering any app. Use AddApp or AddAppsFromAssembly.");
        }

        var services = builder.Services;
        services.AddSingleton(options);
        services.TryAddSingleton(TimeProvider.System);
        services.AddLogging();
        services.TryAddSingleton<McpAppViewManager>();
        services.TryAddScoped<McpAppHost>();
        services.TryAddScoped<IMcpAppHost>(provider => provider.GetRequiredService<McpAppHost>());

        foreach (var app in options.Apps)
        {
            services.AddSingleton(CreateResource(app));
        }

        services.AddSingleton(McpRazorTools.CreateRenderTool);
        services.AddSingleton(McpRazorTools.CreateEventTool);
        services.AddSingleton(McpRazorTools.CreateReleaseTool);

        builder.WithRequestFilters(filters => filters.AddCallToolFilter(McpRazorPrerenderFilter.Create));

        return builder.WithMcpApps();
    }

    private static McpServerResource CreateResource(McpAppRegistration app)
    {
        var shell = McpAppShell.Build(app);
        var meta = new McpUiResourceMeta
        {
            Csp = app.Csp,
            PrefersBorder = app.PrefersBorder ? true : null,
        };
        var hasMeta = meta.Csp is not null || meta.PrefersBorder is not null;
        var metaNode = new JsonObject { ["ui"] = JsonSerializer.SerializeToNode(meta, McpApps.SerializerOptions) };

        var resource = McpServerResource.Create(
            () => new TextResourceContents
            {
                Uri = app.Uri,
                MimeType = McpApps.HtmlMimeType,
                Text = shell,
                Meta = hasMeta ? (JsonObject)metaNode.DeepClone() : null,
            },
            new McpServerResourceCreateOptions
            {
                UriTemplate = app.Uri,
                Name = app.Name,
                Description = app.Description,
                MimeType = McpApps.HtmlMimeType,

                // Set at creation: McpApps.SetResourceUi only updates the template, and resources/list
                // reports a copy of it made when the resource is created.
                Meta = hasMeta ? (JsonObject)metaNode.DeepClone() : null,
            });

        return resource;
    }
}
