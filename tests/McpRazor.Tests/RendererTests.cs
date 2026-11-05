using System.Text.Json;
using McpRazor.Rendering;
using McpRazor.Tests.Components;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace McpRazor.Tests;

public sealed class RendererTests : IAsyncDisposable
{
    private readonly ServiceProvider _services = new ServiceCollection().AddLogging().BuildServiceProvider();
    private McpRazorRenderer? _renderer;

    [Fact]
    public async Task Writes_html_with_escaped_text_markup_and_void_elements()
    {
        var html = await RenderAsync<Markup>();

        Assert.Contains("title=\"A &amp; &quot;B&quot; &lt;c&gt;\"", html);
        Assert.Contains("&lt;b&gt;Tom &amp; Jerry&lt;/b&gt;", html);
        Assert.Contains("<i>trusted</i>", html);
        Assert.Contains("<img src=\"logo.png\" alt=\"Logo\">", html);
        Assert.DoesNotContain("</img>", html);
        Assert.DoesNotContain("</br>", html);
        Assert.Contains("<input disabled>", html);
        Assert.Contains("Ñandú · 你好", html);
    }

    [Fact]
    public async Task Exposes_event_handlers_and_dispatches_clicks()
    {
        var html = await RenderAsync<Counter>();
        Assert.Contains("Count: 0", html);

        await _renderer!.DispatchAsync(Html.ButtonHandler(html, "+1"), EventArgsFactory.Create("click", null));
        await _renderer.DispatchAsync(Html.ButtonHandler((await _renderer.SerializeAsync()).Html, "+1"), EventArgsFactory.Create("click", null));

        var updated = (await _renderer.SerializeAsync()).Html;
        Assert.Contains("Count: 2", updated);
        Assert.Contains("child 2", updated);
    }

    [Fact]
    public async Task Writes_prevent_default_and_stop_propagation_flags()
    {
        var html = await RenderAsync<Counter>();

        Assert.Matches("<a href=\"#\" data-mcp-on-click=\"\\d+\" data-mcp-pd-click data-mcp-sp-click>Reset</a>", html);
        Assert.DoesNotContain("__internal", html);
    }

    [Fact]
    public async Task Dispatches_events_of_child_components()
    {
        var html = await RenderAsync<Counter>();

        await _renderer!.DispatchAsync(Html.ButtonHandler(html, "Child clicks"), EventArgsFactory.Create("click", null));

        Assert.Contains("Child clicks: 1", (await _renderer.SerializeAsync()).Html);
    }

    [Fact]
    public async Task Binds_inputs_textareas_selects_and_checkboxes()
    {
        var html = await RenderAsync<Binding>();
        Assert.Contains("value=\"Ana\"", Html.Tag(html, "name"));
        Assert.Contains(">first &lt;line&gt;</textarea>", html);
        Assert.Contains("<option value=\"blue\" selected>", html);
        Assert.DoesNotContain("<select id=\"color\" value", html);

        await Change(html, "name", "change", "Luis");
        await Change(html, "live", "input", "typing");
        await Change(html, "notes", "change", "second");
        await Change(html, "color", "change", "red");
        await Change(html, "agree", "change", true);
        await Change(html, "amount", "change", "42");

        var updated = (await _renderer!.SerializeAsync()).Html;
        Assert.Equal("Luis|typing|second|red|True|42", Html.Text(updated, "state"));
        Assert.Contains("<option value=\"red\" selected>", updated);
        Assert.Contains(" checked", Html.Tag(updated, "agree"));
    }

    [Fact]
    public async Task Waits_for_async_initialization_and_async_handlers()
    {
        var html = await RenderAsync<AsyncLoad>();
        Assert.Contains("<li>one</li><li>two</li>", html);

        await _renderer!.DispatchAsync(Html.ButtonHandler(html, "Add"), EventArgsFactory.Create("click", null));

        Assert.Contains("<li>three</li>", (await _renderer.SerializeAsync()).Html);
    }

    [Fact]
    public async Task Lists_only_the_handlers_of_the_current_render()
    {
        await RenderAsync<Counter>();
        var first = await _renderer!.SerializeAsync();

        await _renderer.DispatchAsync(Html.ButtonHandler(first.Html, "+1"), EventArgsFactory.Create("click", null));
        var second = await _renderer.SerializeAsync();

        Assert.Equal(3, second.EventHandlerIds.Count);
        Assert.Contains(Html.ButtonHandler(second.Html, "+1"), second.EventHandlerIds);
    }

    public async ValueTask DisposeAsync()
    {
        if (_renderer is not null)
        {
            await _renderer.DisposeAsync();
        }

        await _services.DisposeAsync();
    }

    private async Task<string> RenderAsync<TComponent>() where TComponent : IComponent
    {
        _renderer = new McpRazorRenderer(_services, _services.GetRequiredService<ILoggerFactory>());
        await _renderer.RenderRootAsync(typeof(TComponent), new Dictionary<string, object?>());
        return (await _renderer.SerializeAsync()).Html;
    }

    private async Task Change(string html, string id, string eventName, object value)
    {
        var current = (await _renderer!.SerializeAsync()).Html;
        var payload = JsonSerializer.SerializeToElement(new { value });
        await _renderer.DispatchAsync(Html.Handler(current, id, eventName), EventArgsFactory.Create(eventName, payload));
    }
}
