using System.Text.Json;
using System.Text.Json.Nodes;
using McpRazor.Rendering;
using McpRazor.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Extensions.Apps;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace McpRazor.Mcp;

/// <summary>
/// Renders the first view of an app when the model calls a tool that points to it, and sends it in the
/// tool result's <c>_meta</c>. The app shows it right away, without a round trip, and it works in hosts
/// that do not forward the app's own tool calls to the server.
/// </summary>
internal static class McpRazorPrerenderFilter
{
    /// <summary>The <c>_meta</c> key of the prerendered view in the tool result.</summary>
    public const string MetaKey = "mcpRazor/view";

    public static McpRequestHandler<CallToolRequestParams, CallToolResult> Create(McpRequestHandler<CallToolRequestParams, CallToolResult> next) =>
        async (request, cancellationToken) =>
        {
            var result = await next(request, cancellationToken);

            if (result.IsError is true ||
                request.MatchedPrimitive is not McpServerTool tool ||
                ResourceUriOf(tool) is not { } uri ||
                request.Services?.GetService<McpRazorOptions>() is not { } options ||
                options.Find(uri) is null ||
                !ShouldPrerender(options.Prerender, request.Server.ClientCapabilities) ||
                request.Services.GetService<McpAppViewManager>() is not { } views)
            {
                return result;
            }

            try
            {
                var arguments = request.Params?.Arguments is { } args ? JsonSerializer.SerializeToElement(args) : (JsonElement?)null;
                var view = await views.RenderAsync(uri, result.StructuredContent, arguments, hostContext: null, request.Server.SessionId);

                result.Meta ??= [];
                result.Meta[MetaKey] = new JsonObject
                {
                    ["uri"] = uri,
                    ["viewId"] = view.ViewId,
                    ["html"] = view.Html,
                    ["effects"] = view.Effects,
                };
            }
            catch (Exception exception) when (exception is McpAppRequestException or McpAppRenderException)
            {
                // The app still renders through mcp_razor_render when the host forwards it.
                request.Services.GetService<ILoggerFactory>()?.CreateLogger("McpRazor")
                    .LogWarning(exception, "Could not prerender the MCP App {Uri} for tool {Tool}.", uri, tool.ProtocolTool.Name);
            }

            return result;
        };

    private static bool ShouldPrerender(McpAppPrerender mode, ClientCapabilities? capabilities) => mode switch
    {
        McpAppPrerender.Always => true,
        McpAppPrerender.Never => false,
        // Unknown capabilities (stateless HTTP has no session to remember them): prerender, as the app may need it.
        _ => capabilities is null || McpApps.GetUiCapability(capabilities) is not null,
    };

    private static string? ResourceUriOf(McpServerTool tool) =>
        tool.ProtocolTool.Meta?["ui"]?["resourceUri"] is JsonValue value && value.TryGetValue<string>(out var uri) ? uri : null;
}
