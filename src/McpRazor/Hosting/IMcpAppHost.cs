using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace McpRazor;

/// <summary>
/// The MCP host (Claude, VS Code…) that shows the app. Available to any component of the app through <c>[Inject]</c>.
/// </summary>
/// <remarks>
/// Requests are queued and the runtime sends them to the host when the current render or event finishes.
/// The host decides what to do with them: it may ask the user for consent or ignore them.
/// </remarks>
public interface IMcpAppHost
{
    /// <summary>What the host told the app about itself: theme, locale, time zone, display mode…</summary>
    McpHostContext Context { get; }

    /// <summary>
    /// The culture the app runs with: the host's locale, or <see cref="McpRazorOptions.DefaultCulture"/> when the host
    /// sends none. It is also <c>CultureInfo.CurrentCulture</c> while the app renders and handles events, so
    /// <c>ToString("C")</c> and friends follow it.
    /// </summary>
    CultureInfo Culture { get; }

    /// <summary>The arguments of the tool call that opened this app, if the host sent them.</summary>
    JsonElement? ToolInput { get; }

    /// <summary>
    /// Replaces the context this app gives the model for its next turns (<c>ui/update-model-context</c>).
    /// Use it to tell the model what the user did, for example "The user approved loan 1042".
    /// </summary>
    void UpdateModelContext(string text, JsonNode? structuredContent = null);

    /// <summary>Posts a message to the chat as the user (<c>ui/message</c>). Hosts usually answer it right away.</summary>
    void SendMessage(string text);

    /// <summary>Asks the host to open a URL in the browser (<c>ui/open-link</c>).</summary>
    void OpenLink(string url);

    /// <summary>Asks the host to switch display mode: <c>inline</c>, <c>fullscreen</c> or <c>pip</c>.</summary>
    void RequestDisplayMode(string mode);
}

/// <summary>The part of the host context (<c>hostContext</c> in <c>ui/initialize</c>) that components usually need.</summary>
/// <param name="Theme"><c>light</c> or <c>dark</c>.</param>
/// <param name="Locale">BCP 47 language tag, for example <c>es-ES</c>.</param>
/// <param name="TimeZone">IANA time zone, for example <c>Europe/Madrid</c>.</param>
/// <param name="DisplayMode"><c>inline</c>, <c>fullscreen</c> or <c>pip</c>.</param>
/// <param name="Platform"><c>web</c>, <c>desktop</c> or <c>mobile</c>.</param>
public sealed record McpHostContext(
    string? Theme = null,
    string? Locale = null,
    string? TimeZone = null,
    string? DisplayMode = null,
    string? Platform = null)
{
    /// <summary>The culture of <see cref="Locale"/>, or <see langword="null"/> if it is missing or unknown.</summary>
    public CultureInfo? LocaleCulture
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Locale))
            {
                return null;
            }

            try
            {
                return CultureInfo.GetCultureInfo(Locale.Trim());
            }
            catch (CultureNotFoundException)
            {
                return null;
            }
        }
    }

    /// <summary>An empty context, used when the host sends none.</summary>
    public static McpHostContext Empty { get; } = new();

    internal static McpHostContext From(JsonElement? hostContext)
    {
        if (hostContext is not { ValueKind: JsonValueKind.Object } context)
        {
            return Empty;
        }

        return new McpHostContext(
            Read(context, "theme"),
            Read(context, "locale"),
            Read(context, "timeZone"),
            Read(context, "displayMode"),
            Read(context, "platform"));

        static string? Read(JsonElement element, string name) =>
            element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }
}
