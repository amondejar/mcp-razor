using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using McpRazor.Hosting;
using McpRazor.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace McpRazor.Views;

/// <summary>
/// Keeps the live views (one component tree per app shown in a chat) and runs renders and events on them.
/// </summary>
internal sealed class McpAppViewManager : IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, McpAppView> _views = new(StringComparer.Ordinal);
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger _logger;
    private readonly McpRazorOptions _options;
    private readonly TimeProvider _time;

    public McpAppViewManager(IServiceScopeFactory scopeFactory, ILoggerFactory loggerFactory, McpRazorOptions options, TimeProvider time)
    {
        _scopeFactory = scopeFactory;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger("McpRazor");
        _options = options;
        _time = time;
    }

    public int Count => _views.Count;

    /// <summary>Creates a view of the app at <paramref name="uri"/> and renders it for the first time.</summary>
    public async Task<ViewResult> RenderAsync(string uri, JsonElement? data, JsonElement? toolInput, JsonElement? hostContext, string? owner)
    {
        var app = _options.Find(uri) ?? throw new McpAppRequestException($"No MCP App is registered at '{uri}'.");

        await EvictAsync();

        var parameters = new Dictionary<string, object?>();
        if (app.DataType is { } dataType)
        {
            parameters["Data"] = DeserializeData(data, dataType, uri);
        }

        var scope = _scopeFactory.CreateAsyncScope();
        var host = scope.ServiceProvider.GetRequiredService<McpAppHost>();
        host.Context = McpHostContext.From(hostContext);
        host.ToolInput = toolInput is { ValueKind: not (JsonValueKind.Undefined or JsonValueKind.Null) } input ? input.Clone() : null;

        var renderer = new McpRazorRenderer(scope.ServiceProvider, _loggerFactory);
        var view = new McpAppView(NewViewId(), uri, owner, scope, renderer, host, _time.GetUtcNow());
        ApplyCulture(view);

        _logger.LogInformation(
            "Rendering MCP App {Uri} (view {ViewId}): host locale {Locale}, theme {Theme}, time zone {TimeZone}; culture {Culture}.",
            uri, view.Id, host.Context.Locale ?? "(not sent)", host.Context.Theme ?? "(not sent)",
            host.Context.TimeZone ?? "(not sent)", view.Renderer.Culture.Name.Length == 0 ? "invariant" : view.Renderer.Culture.Name);

        try
        {
            await renderer.RenderRootAsync(app.ComponentType, parameters);
            var html = await renderer.SerializeAsync();
            view.EventHandlerIds = html.EventHandlerIds;
            _views[view.Id] = view;
            return new ViewResult(view.Id, html.Html, host.DrainEffects());
        }
        catch
        {
            await view.DisposeAsync();
            throw;
        }
    }

    /// <summary>Runs the event handler <paramref name="eventHandlerId"/> of a view and renders it again.</summary>
    public async Task<ViewResult> DispatchAsync(string viewId, ulong eventHandlerId, string eventType, JsonElement? payload, JsonElement? hostContext, string? owner)
    {
        var view = GetView(viewId, owner);

        await view.Gate.WaitAsync();
        try
        {
            if (view.Disposed)
            {
                throw new McpAppViewExpiredException();
            }

            // Only handlers present in the HTML the app was given can be invoked.
            if (!view.EventHandlerIds.Contains(eventHandlerId))
            {
                throw new McpAppRequestException("The event handler is not part of the current view.");
            }

            view.LastUsed = _time.GetUtcNow();
            if (hostContext is { ValueKind: JsonValueKind.Object })
            {
                view.Host.Context = McpHostContext.From(hostContext);
                ApplyCulture(view);
            }

            await view.Renderer.DispatchAsync(eventHandlerId, EventArgsFactory.Create(eventType, payload));
            var html = await view.Renderer.SerializeAsync();
            view.EventHandlerIds = html.EventHandlerIds;
            return new ViewResult(view.Id, html.Html, view.Host.DrainEffects());
        }
        finally
        {
            view.Gate.Release();
        }
    }

    /// <summary>Releases a view, for example when the host tears the app down.</summary>
    public async Task<bool> ReleaseAsync(string viewId, string? owner)
    {
        if (!_views.TryGetValue(viewId, out var view) || !OwnedBy(view, owner))
        {
            return false;
        }

        return await RemoveAsync(view, "released");
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var view in _views.Values)
        {
            await view.DisposeAsync();
        }

        _views.Clear();
    }

    /// <summary>Releases idle views, and the least recently used ones when <see cref="McpRazorOptions.MaxViews"/> is reached.</summary>
    internal async Task EvictAsync()
    {
        var now = _time.GetUtcNow();
        foreach (var view in _views.Values)
        {
            if (now - view.LastUsed > _options.ViewIdleTimeout)
            {
                await RemoveAsync(view, "idle");
            }
        }

        var excess = _views.Count - Math.Max(0, _options.MaxViews - 1);
        if (excess > 0)
        {
            foreach (var view in _views.Values.OrderBy(v => v.LastUsed).Take(excess).ToList())
            {
                await RemoveAsync(view, "capacity");
            }
        }
    }

    private async Task<bool> RemoveAsync(McpAppView view, string reason)
    {
        if (!_views.TryRemove(new KeyValuePair<string, McpAppView>(view.Id, view)))
        {
            return false;
        }

        _logger.LogDebug("Released MCP App view {ViewId} of {Uri} ({Reason}).", view.Id, view.Uri, reason);

        // Wait for an event in flight, so the renderer is not disposed under it.
        await view.Gate.WaitAsync();
        try
        {
            await view.DisposeAsync();
        }
        finally
        {
            view.Gate.Release();
        }

        return true;
    }

    private McpAppView GetView(string viewId, string? owner)
    {
        if (string.IsNullOrEmpty(viewId) || !_views.TryGetValue(viewId, out var view))
        {
            throw new McpAppViewExpiredException();
        }

        if (!OwnedBy(view, owner))
        {
            // Same answer as a missing view: do not reveal that the ID exists.
            throw new McpAppViewExpiredException();
        }

        return view;
    }

    private void ApplyCulture(McpAppView view)
    {
        var culture = view.Host.Context.LocaleCulture ?? _options.DefaultCulture ?? CultureInfo.CurrentCulture;
        view.Host.Culture = culture;
        view.Renderer.Culture = culture;
    }

    private static bool OwnedBy(McpAppView view, string? owner) => string.Equals(view.Owner, owner, StringComparison.Ordinal);

    private object? DeserializeData(JsonElement? data, Type dataType, string uri)
    {
        if (data is not { ValueKind: not (JsonValueKind.Undefined or JsonValueKind.Null) } element)
        {
            return dataType.IsValueType ? Activator.CreateInstance(dataType) : null;
        }

        try
        {
            return element.Deserialize(dataType, _options.SerializerOptions);
        }
        catch (JsonException exception)
        {
            throw new McpAppRequestException($"The structured content does not match the data type {dataType.Name} of '{uri}': {exception.Message}");
        }
    }

    private static string NewViewId() => Base64Url(RandomNumberGenerator.GetBytes(18));

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

/// <summary>The HTML of a view and the requests it queued for the host.</summary>
internal sealed record ViewResult(string ViewId, string Html, JsonArray Effects);

internal sealed class McpAppView(string id, string uri, string? owner, AsyncServiceScope scope, McpRazorRenderer renderer, McpAppHost host, DateTimeOffset created) : IAsyncDisposable
{
    public string Id { get; } = id;
    public string Uri { get; } = uri;
    public string? Owner { get; } = owner;
    public McpRazorRenderer Renderer { get; } = renderer;
    public McpAppHost Host { get; } = host;
    public SemaphoreSlim Gate { get; } = new(1, 1);
    public DateTimeOffset LastUsed { get; set; } = created;
    public IReadOnlySet<ulong> EventHandlerIds { get; set; } = new HashSet<ulong>();
    public bool Disposed { get; private set; }

    public async ValueTask DisposeAsync()
    {
        if (Disposed)
        {
            return;
        }

        Disposed = true;
        await Renderer.DisposeAsync();
        await scope.DisposeAsync();
    }
}

/// <summary>The request from the app is not valid. The message is safe to show to the user.</summary>
internal class McpAppRequestException(string message) : Exception(message);

/// <summary>The view no longer exists on the server: it expired, was released, or the server restarted.</summary>
internal sealed class McpAppViewExpiredException()
    : McpAppRequestException("This view has expired. Ask the assistant to show it again.");
