using System.Text.Json;
using McpRazor.Hosting;
using McpRazor.Rendering;
using McpRazor.Tests.Components;
using McpRazor.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;

namespace McpRazor.Tests;

public sealed class ViewManagerTests : IAsyncDisposable
{
    private const string OrderUri = "ui://test/order";

    private readonly ManualTimeProvider _time = new(new DateTimeOffset(2026, 11, 5, 9, 0, 0, TimeSpan.Zero));
    private readonly McpRazorOptions _options = new();
    private readonly ServiceProvider _services;
    private readonly McpAppViewManager _views;

    public ViewManagerTests()
    {
        _options.AddApp<OrderApp>(OrderUri).AddApp<Counter>("ui://test/counter");
        _services = new ServiceCollection()
            .AddLogging()
            .AddSingleton(_options)
            .AddScoped<McpAppHost>()
            .AddScoped<IMcpAppHost>(provider => provider.GetRequiredService<McpAppHost>())
            .BuildServiceProvider();
        _views = new McpAppViewManager(
            _services.GetRequiredService<IServiceScopeFactory>(),
            _services.GetRequiredService<ILoggerFactory>(),
            _options,
            _time);
    }

    [Fact]
    public async Task Renders_the_tool_data_with_the_serializer_options_of_the_mcp_sdk()
    {
        // The SDK writes enums as strings and camelCase names; the view has to read them back.
        var data = JsonSerializer.SerializeToElement(new OrderData("A-7", OrderStatus.Draft, 10m), McpJsonUtilities.DefaultOptions);
        Assert.Equal("Draft", data.GetProperty("status").GetString());

        var view = await RenderOrderAsync(data);

        Assert.Contains("<h1>Order A-7</h1>", view.Html);
        Assert.Equal("Draft", Html.Text(view.Html, "status"));
        Assert.Equal("es-ES|dark|A-7", Html.Text(view.Html, "locale"));
        Assert.Empty(view.Effects);
        Assert.Equal(1, _views.Count);
    }

    [Fact]
    public async Task Runs_events_and_returns_the_requests_queued_for_the_host()
    {
        var view = await RenderOrderAsync();

        var result = await _views.DispatchAsync(view.ViewId, Html.Handler(view.Html, "confirm", "click"), "click", null, null, owner: null);

        Assert.Equal("Confirmed", Html.Text(result.Html, "status"));
        Assert.Equal(2, result.Effects.Count);
        Assert.Equal("update-model-context", (string?)result.Effects[0]!["type"]);
        Assert.Equal("Order A-7 confirmed.", (string?)result.Effects[0]!["text"]);
        Assert.Equal("A-7", (string?)result.Effects[0]!["structuredContent"]!["id"]);
        Assert.Equal("message", (string?)result.Effects[1]!["type"]);
    }

    [Fact]
    public async Task Rejects_handlers_that_are_not_in_the_current_view()
    {
        var view = await RenderOrderAsync();

        var exception = await Assert.ThrowsAsync<McpAppRequestException>(() =>
            _views.DispatchAsync(view.ViewId, 999, "click", null, null, owner: null));

        Assert.Equal("The event handler is not part of the current view.", exception.Message);
    }

    [Fact]
    public async Task Hides_views_of_other_sessions()
    {
        var view = await RenderOrderAsync(owner: "session-a");
        var handler = Html.Handler(view.Html, "confirm", "click");

        await Assert.ThrowsAsync<McpAppViewExpiredException>(() => _views.DispatchAsync(view.ViewId, handler, "click", null, null, owner: "session-b"));
        Assert.False(await _views.ReleaseAsync(view.ViewId, owner: "session-b"));

        var result = await _views.DispatchAsync(view.ViewId, handler, "click", null, null, owner: "session-a");
        Assert.Equal("Confirmed", Html.Text(result.Html, "status"));
    }

    [Fact]
    public async Task Reports_unknown_apps_and_views()
    {
        await Assert.ThrowsAsync<McpAppRequestException>(() => _views.RenderAsync("ui://nope", null, null, null, null));
        await Assert.ThrowsAsync<McpAppViewExpiredException>(() => _views.DispatchAsync("missing", 1, "click", null, null, null));
    }

    [Fact]
    public async Task Reports_data_that_does_not_match_the_component()
    {
        var data = JsonSerializer.SerializeToElement(new { id = "A-1", status = "Teleported" });

        var exception = await Assert.ThrowsAsync<McpAppRequestException>(() => _views.RenderAsync(OrderUri, data, null, null, null));

        Assert.Contains("does not match the data type OrderData", exception.Message);
        Assert.Equal(0, _views.Count);
    }

    [Fact]
    public async Task Wraps_component_failures_without_leaking_them_into_the_view()
    {
        var view = await RenderOrderAsync();

        var exception = await Assert.ThrowsAsync<McpAppRenderException>(() =>
            _views.DispatchAsync(view.ViewId, Html.Handler(view.Html, "fail", "click"), "click", null, null, owner: null));

        Assert.IsType<InvalidOperationException>(exception.InnerException);
    }

    [Fact]
    public async Task Releases_idle_views()
    {
        _options.ViewIdleTimeout = TimeSpan.FromMinutes(10);
        var old = await RenderOrderAsync();

        _time.Advance(TimeSpan.FromMinutes(11));
        var fresh = await RenderOrderAsync();

        Assert.Equal(1, _views.Count);
        await Assert.ThrowsAsync<McpAppViewExpiredException>(() =>
            _views.DispatchAsync(old.ViewId, Html.Handler(old.Html, "confirm", "click"), "click", null, null, null));
        var result = await _views.DispatchAsync(fresh.ViewId, Html.Handler(fresh.Html, "confirm", "click"), "click", null, null, null);
        Assert.Equal("Confirmed", Html.Text(result.Html, "status"));
    }

    [Fact]
    public async Task Releases_the_least_recently_used_view_at_capacity()
    {
        _options.MaxViews = 2;
        var first = await RenderOrderAsync();
        _time.Advance(TimeSpan.FromSeconds(1));
        var second = await RenderOrderAsync();
        _time.Advance(TimeSpan.FromSeconds(1));

        // Using the first view makes the second one the least recently used.
        await _views.DispatchAsync(first.ViewId, Html.Handler(first.Html, "confirm", "click"), "click", null, null, null);
        _time.Advance(TimeSpan.FromSeconds(1));
        await RenderOrderAsync();

        Assert.Equal(2, _views.Count);
        await Assert.ThrowsAsync<McpAppViewExpiredException>(() =>
            _views.DispatchAsync(second.ViewId, Html.Handler(second.Html, "confirm", "click"), "click", null, null, null));
    }

    [Fact]
    public async Task Releases_views_on_request()
    {
        var view = await RenderOrderAsync();

        Assert.True(await _views.ReleaseAsync(view.ViewId, owner: null));
        Assert.False(await _views.ReleaseAsync(view.ViewId, owner: null));
        Assert.Equal(0, _views.Count);
    }

    [Fact]
    public async Task Gives_each_view_its_own_component_state()
    {
        var a = await _views.RenderAsync("ui://test/counter", null, null, null, null);
        var b = await _views.RenderAsync("ui://test/counter", null, null, null, null);

        var updated = await _views.DispatchAsync(a.ViewId, Html.ButtonHandler(a.Html, "+1"), "click", null, null, null);

        Assert.NotEqual(a.ViewId, b.ViewId);
        Assert.Contains("Count: 1", updated.Html);
        Assert.Contains("Count: 0", b.Html);
    }

    public async ValueTask DisposeAsync()
    {
        await _views.DisposeAsync();
        await _services.DisposeAsync();
    }

    private Task<ViewResult> RenderOrderAsync(JsonElement? data = null, string? owner = null) =>
        _views.RenderAsync(
            OrderUri,
            data ?? JsonSerializer.SerializeToElement(new OrderData("A-7", OrderStatus.Draft, 10m), McpJsonUtilities.DefaultOptions),
            JsonSerializer.SerializeToElement(new { id = "A-7" }),
            JsonSerializer.SerializeToElement(new { theme = "dark", locale = "es-ES" }),
            owner);
}
