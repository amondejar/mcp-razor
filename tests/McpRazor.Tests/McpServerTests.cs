using System.IO.Pipelines;
using System.Text.Json;
using McpRazor.Tests.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Extensions.Apps;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace McpRazor.Tests;

/// <summary>Runs a real MCP server with McpRazor and talks to it with the SDK client over in-memory streams.</summary>
public sealed class McpServerTests : IAsyncLifetime
{
    private readonly Pipe _clientToServer = new();
    private readonly Pipe _serverToClient = new();
    private IHost _host = null!;
    private McpClient _client = null!;

    public async Task InitializeAsync()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();

        var showOrder = McpServerTool.Create(
            (string id) => new OrderData(id, OrderStatus.Draft, 25m),
            new McpServerToolCreateOptions { Name = "show_order", Description = "Shows an order.", UseStructuredContent = true });
        McpApps.SetAppUi(showOrder, new McpUiToolMeta { ResourceUri = "ui://test/order" });

        builder.Services.AddMcpServer()
            .WithStreamServerTransport(_clientToServer.Reader.AsStream(), _serverToClient.Writer.AsStream())
            .WithTools([showOrder])
            .WithRazorApps(apps => apps.AddAppsFromAssembly(typeof(OrderApp).Assembly));

        _host = builder.Build();
        await _host.StartAsync();
        _client = await McpClient.CreateAsync(new StreamClientTransport(_clientToServer.Writer.AsStream(), _serverToClient.Reader.AsStream()));
    }

    [Fact]
    public void Advertises_mcp_apps_support()
    {
        var extensions = _client.ServerCapabilities.Extensions;
        Assert.NotNull(extensions);
        Assert.Contains(McpApps.ExtensionId, extensions.Keys);
    }

    [Fact]
    public async Task Adds_app_only_tools_next_to_the_model_tools()
    {
        var tools = (await _client.ListToolsAsync(new ListToolsRequestParams())).Tools.ToDictionary(tool => tool.Name);

        Assert.Equal("ui://test/order", tools["show_order"].Meta!["ui"]!["resourceUri"]!.GetValue<string>());
        foreach (var name in new[] { "mcp_razor_render", "mcp_razor_event", "mcp_razor_release" })
        {
            var visibility = tools[name].Meta!["ui"]!["visibility"]!.AsArray().Select(node => node!.GetValue<string>());
            Assert.Equal(new[] { "app" }, visibility);
        }
    }

    [Fact]
    public async Task Serves_the_app_shell_as_an_mcp_app_resource()
    {
        var listed = Assert.Single((await _client.ListResourcesAsync(new ListResourcesRequestParams())).Resources);
        Assert.Equal("ui://test/order", listed.Uri);
        Assert.Equal("text/html;profile=mcp-app", listed.MimeType);
        Assert.True(listed.Meta!["ui"]!["prefersBorder"]!.GetValue<bool>());

        var content = Assert.IsType<TextResourceContents>(Assert.Single((await _client.ReadResourceAsync("ui://test/order")).Contents));
        Assert.Equal("text/html;profile=mcp-app", content.MimeType);
        Assert.True(content.Meta!["ui"]!["prefersBorder"]!.GetValue<bool>());
        Assert.Contains("data-mcp-app=\"ui://test/order\"", content.Text);
    }

    [Fact]
    public async Task Renders_the_tool_result_and_runs_events_end_to_end()
    {
        // 1. The model calls the tool.
        var toolResult = await _client.CallToolAsync("show_order", new Dictionary<string, object?> { ["id"] = "B-12" });
        var data = toolResult.StructuredContent!.Value;

        // 2. The app asks the server to render it.
        var render = await CallAppToolAsync("mcp_razor_render", new()
        {
            ["uri"] = "ui://test/order",
            ["data"] = data,
            ["input"] = JsonSerializer.SerializeToElement(new { id = "B-12" }),
            ["hostContext"] = JsonSerializer.SerializeToElement(new { locale = "en-GB", theme = "light" }),
        });
        Assert.Contains("<h1>Order B-12</h1>", render.GetProperty("html").GetString());
        var viewId = render.GetProperty("viewId").GetString();

        // 3. The user clicks Confirm.
        var confirm = Html.Handler(render.GetProperty("html").GetString()!, "confirm", "click");
        var afterClick = await CallAppToolAsync("mcp_razor_event", new() { ["viewId"] = viewId, ["handler"] = confirm, ["type"] = "click" });
        Assert.Equal("Confirmed", Html.Text(afterClick.GetProperty("html").GetString()!, "status"));
        Assert.Equal("update-model-context", afterClick.GetProperty("effects")[0].GetProperty("type").GetString());

        // 4. The host tears the app down.
        var release = await CallAppToolAsync("mcp_razor_release", new() { ["viewId"] = viewId });
        Assert.True(release.GetProperty("released").GetBoolean());
    }

    [Fact]
    public async Task Returns_tool_errors_without_exception_details()
    {
        var render = await CallAppToolAsync("mcp_razor_render", new()
        {
            ["uri"] = "ui://test/order",
            ["data"] = JsonSerializer.SerializeToElement(new { id = "C-1", status = "Draft", total = 1 }),
        });
        var viewId = render.GetProperty("viewId").GetString();
        var fail = Html.Handler(render.GetProperty("html").GetString()!, "fail", "click");

        var result = await _client.CallToolAsync("mcp_razor_event", new Dictionary<string, object?> { ["viewId"] = viewId, ["handler"] = fail, ["type"] = "click" });

        Assert.True(result.IsError);
        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        Assert.Equal("The app failed on the server. The error has been logged.", text);
        Assert.DoesNotContain("Secret", text);

        var expired = await _client.CallToolAsync("mcp_razor_event", new Dictionary<string, object?> { ["viewId"] = "nope", ["handler"] = 1, ["type"] = "click" });
        Assert.True(expired.IsError);
        Assert.Contains("expired", Assert.IsType<TextContentBlock>(Assert.Single(expired.Content)).Text);
    }

    public async Task DisposeAsync()
    {
        await _client.DisposeAsync();
        await _host.StopAsync();
        _host.Dispose();
    }

    private async Task<JsonElement> CallAppToolAsync(string name, Dictionary<string, object?> arguments)
    {
        var result = await _client.CallToolAsync(name, arguments);
        Assert.True(result.IsError is not true, result.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text);
        var envelope = result.StructuredContent!.Value;
        Assert.Equal(1, envelope.GetProperty("mcpRazor").GetInt32());
        return envelope;
    }
}
