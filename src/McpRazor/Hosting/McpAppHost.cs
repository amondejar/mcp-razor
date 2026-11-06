using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace McpRazor.Hosting;

/// <summary>Per-view implementation of <see cref="IMcpAppHost"/>. Requests are queued as effects for the runtime.</summary>
internal sealed class McpAppHost : IMcpAppHost
{
    private readonly List<JsonObject> _effects = [];
    private readonly Lock _gate = new();

    public McpHostContext Context { get; internal set; } = McpHostContext.Empty;

    public JsonElement? ToolInput { get; internal set; }

    public CultureInfo Culture { get; internal set; } = CultureInfo.CurrentCulture;

    public void UpdateModelContext(string text, JsonNode? structuredContent = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        var effect = new JsonObject { ["type"] = "update-model-context", ["text"] = text };
        if (structuredContent is not null)
        {
            effect["structuredContent"] = structuredContent.DeepClone();
        }

        Add(effect);
    }

    public void SendMessage(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        Add(new JsonObject { ["type"] = "message", ["text"] = text });
    }

    public void OpenLink(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed) || (parsed.Scheme != Uri.UriSchemeHttps && parsed.Scheme != Uri.UriSchemeHttp))
        {
            throw new ArgumentException("Only absolute http or https URLs can be opened.", nameof(url));
        }

        Add(new JsonObject { ["type"] = "open-link", ["url"] = parsed.ToString() });
    }

    public void RequestDisplayMode(string mode)
    {
        if (mode is not ("inline" or "fullscreen" or "pip"))
        {
            throw new ArgumentException("The display mode must be inline, fullscreen or pip.", nameof(mode));
        }

        Add(new JsonObject { ["type"] = "display-mode", ["mode"] = mode });
    }

    /// <summary>Returns and clears the queued effects.</summary>
    internal JsonArray DrainEffects()
    {
        lock (_gate)
        {
            var array = new JsonArray();
            foreach (var effect in _effects)
            {
                array.Add(effect);
            }

            _effects.Clear();
            return array;
        }
    }

    private void Add(JsonObject effect)
    {
        lock (_gate)
        {
            _effects.Add(effect);
        }
    }
}
