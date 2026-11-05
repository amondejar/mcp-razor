using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using McpRazor.Rendering;
using McpRazor.Views;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Extensions.Apps;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace McpRazor.Mcp;

/// <summary>
/// The app-only tools the runtime calls through the host. They are marked <c>visibility: ["app"]</c>, so hosts
/// hide them from the model, but that is enforced by the host: the server treats every call as untrusted input.
/// </summary>
internal static class McpRazorTools
{
    public const string RenderToolName = "mcp_razor_render";
    public const string EventToolName = "mcp_razor_event";
    public const string ReleaseToolName = "mcp_razor_release";

    public static McpServerTool CreateRenderTool(IServiceProvider services) =>
        AppOnly(McpServerTool.Create(RenderAsync, new McpServerToolCreateOptions
        {
            Services = services,
            Name = RenderToolName,
            Title = "Render MCP App view",
            Description = "Internal to McpRazor apps: renders a Razor component view. Called by the app, not by the model.",
            ReadOnly = true,
        }));

    public static McpServerTool CreateEventTool(IServiceProvider services) =>
        AppOnly(McpServerTool.Create(DispatchAsync, new McpServerToolCreateOptions
        {
            Services = services,
            Name = EventToolName,
            Title = "Handle MCP App event",
            Description = "Internal to McpRazor apps: runs a UI event handler of a view. Called by the app, not by the model.",
        }));

    public static McpServerTool CreateReleaseTool(IServiceProvider services) =>
        AppOnly(McpServerTool.Create(ReleaseAsync, new McpServerToolCreateOptions
        {
            Services = services,
            Name = ReleaseToolName,
            Title = "Release MCP App view",
            Description = "Internal to McpRazor apps: releases a view. Called by the app, not by the model.",
            Idempotent = true,
        }));

    private static McpServerTool AppOnly(McpServerTool tool) =>
        McpApps.SetAppUi(tool, new McpUiToolMeta { Visibility = [McpUiToolVisibility.App] });

    private static Task<CallToolResult> RenderAsync(
        McpAppViewManager views,
        McpRazorOptions options,
        ILogger<McpAppViewManager> logger,
        McpServer server,
        [Description("The ui:// URI of the app.")] string uri,
        [Description("The structuredContent of the tool result.")] JsonElement? data = null,
        [Description("The arguments of the tool call.")] JsonElement? input = null,
        [Description("The host context.")] JsonElement? hostContext = null) =>
        RunAsync(options, logger, async () =>
            await views.RenderAsync(uri, data, input, hostContext, server.SessionId));

    private static Task<CallToolResult> DispatchAsync(
        McpAppViewManager views,
        McpRazorOptions options,
        ILogger<McpAppViewManager> logger,
        McpServer server,
        [Description("The view ID.")] string viewId,
        [Description("The event handler ID.")] ulong handler,
        [Description("The DOM event type, for example click or change.")] string type,
        [Description("Event details, for example the new value.")] JsonElement? payload = null,
        [Description("The host context.")] JsonElement? hostContext = null) =>
        RunAsync(options, logger, async () =>
            await views.DispatchAsync(viewId, handler, type, payload, hostContext, server.SessionId));

    private static async Task<CallToolResult> ReleaseAsync(
        McpAppViewManager views,
        McpServer server,
        [Description("The view ID.")] string viewId)
    {
        var released = await views.ReleaseAsync(viewId, server.SessionId);
        var envelope = new JsonObject { ["mcpRazor"] = 1, ["viewId"] = viewId, ["released"] = released };
        return new CallToolResult
        {
            StructuredContent = JsonSerializer.SerializeToElement(envelope),
            Content = [new TextContentBlock { Text = released ? "Released." : "Nothing to release." }],
        };
    }

    private static async Task<CallToolResult> RunAsync(McpRazorOptions options, ILogger logger, Func<Task<ViewResult>> work)
    {
        try
        {
            var result = await work();
            var envelope = new JsonObject
            {
                ["mcpRazor"] = 1,
                ["viewId"] = result.ViewId,
                ["html"] = result.Html,
                ["effects"] = result.Effects,
            };

            return new CallToolResult
            {
                StructuredContent = JsonSerializer.SerializeToElement(envelope),
                Content = [new TextContentBlock { Text = "View rendered." }],
            };
        }
        catch (McpAppRequestException exception)
        {
            return Error(exception.Message);
        }
        catch (McpAppRenderException exception)
        {
            logger.LogError(exception.InnerException ?? exception, "An MCP App component failed.");
            return Error(options.DetailedErrors && exception.InnerException is { } inner
                ? $"{inner.GetType().Name}: {inner.Message}"
                : "The app failed on the server. The error has been logged.");
        }
    }

    private static CallToolResult Error(string message) => new()
    {
        IsError = true,
        Content = [new TextContentBlock { Text = message }],
    };
}
