using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.Extensions.Logging;

namespace McpRazor.Rendering;

/// <summary>
/// A Blazor renderer that keeps a live component tree in memory and serializes it to HTML on demand.
/// Unlike <c>HtmlRenderer</c>, it keeps the event handler IDs of the render tree, so <c>@onclick</c>,
/// <c>@bind</c> and friends can be dispatched later from the MCP App running in the host.
/// </summary>
internal sealed class McpRazorRenderer : Renderer
{
    private readonly Dispatcher _dispatcher = Dispatcher.CreateDefault();
    private Exception? _failure;

    public McpRazorRenderer(IServiceProvider services, ILoggerFactory loggerFactory)
        : base(services, loggerFactory)
    {
    }

    public override Dispatcher Dispatcher => _dispatcher;

    /// <summary>Components see an interactive renderer: event handlers work and they can re-render.</summary>
    protected override RendererInfo RendererInfo { get; } = new("McpApp", isInteractive: true);

    public int RootComponentId { get; private set; } = -1;

    /// <summary>The culture components run with: <c>CultureInfo.CurrentCulture</c> and <c>CurrentUICulture</c>.</summary>
    public CultureInfo Culture { get; set; } = CultureInfo.CurrentCulture;

    /// <summary>Instantiates <paramref name="componentType"/> as the root and waits until it is fully rendered,
    /// including asynchronous lifecycle methods such as <c>OnInitializedAsync</c>.</summary>
    public Task RenderRootAsync(Type componentType, IDictionary<string, object?> parameters) =>
        Dispatcher.InvokeAsync(async () =>
        {
            ApplyCulture();
            if (RootComponentId != -1)
            {
                throw new InvalidOperationException("The root component has already been rendered.");
            }

            var component = InstantiateComponent(componentType);
            RootComponentId = AssignRootComponentId(component);
            await RenderRootComponentAsync(RootComponentId, ParameterView.FromDictionary(parameters));
            ThrowIfFailed();
        });

    /// <summary>Dispatches a DOM event to the handler with the given ID and waits for it to finish.</summary>
    public Task DispatchAsync(ulong eventHandlerId, EventArgs eventArgs) =>
        Dispatcher.InvokeAsync(async () =>
        {
            ApplyCulture();
            await DispatchEventAsync(eventHandlerId, fieldInfo: null, eventArgs);
            ThrowIfFailed();
        });

    /// <summary>Serializes the current component tree to HTML.</summary>
    public Task<RenderedHtml> SerializeAsync() =>
        Dispatcher.InvokeAsync(() =>
        {
            ThrowIfFailed();
            return HtmlWriter.Write(this, RootComponentId);
        });

    internal ArrayRange<RenderTreeFrame> GetFrames(int componentId) => GetCurrentRenderTreeFrames(componentId);

    protected override void HandleException(Exception exception) => _failure ??= exception;

    // The culture flows with the async context, so it also applies to awaited work and re-renders.
    private void ApplyCulture()
    {
        CultureInfo.CurrentCulture = Culture;
        CultureInfo.CurrentUICulture = Culture;
    }

    protected override Task UpdateDisplayAsync(in RenderBatch renderBatch) => Task.CompletedTask;

    private void ThrowIfFailed()
    {
        if (_failure is { } failure)
        {
            _failure = null;
            throw new McpAppRenderException("The component threw an unhandled exception.", failure);
        }
    }
}

/// <summary>The serialized HTML of a view and the event handler IDs it exposes.</summary>
internal sealed record RenderedHtml(string Html, IReadOnlySet<ulong> EventHandlerIds);

/// <summary>A component of an MCP App failed while rendering or handling an event.</summary>
public sealed class McpAppRenderException : Exception
{
    /// <summary>Creates the exception.</summary>
    public McpAppRenderException(string message, Exception? inner = null) : base(message, inner)
    {
    }
}
