using System.Text;
using System.Text.Encodings.Web;

namespace McpRazor.Runtime;

/// <summary>
/// Builds the static HTML document served at an app's <c>ui://</c> URI: base styles, an empty root and the runtime.
/// It is the same for every call, so hosts can prefetch and cache it; the content arrives later through the runtime.
/// </summary>
internal static class McpAppShell
{
    private static readonly Lazy<string> Script = new(() => ReadResource("McpRazor.Runtime.mcp-razor.js"));
    private static readonly Lazy<string> Styles = new(() => ReadResource("McpRazor.Runtime.mcp-razor.css"));

    public static string Build(McpAppRegistration app)
    {
        var encoder = HtmlEncoder.Default;
        var html = new StringBuilder(Script.Value.Length + Styles.Value.Length + 1024);
        html.Append("<!DOCTYPE html>\n<html lang=\"en\">\n<head>\n<meta charset=\"utf-8\">\n")
            .Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n")
            .Append("<meta name=\"generator\" content=\"McpRazor\">\n")
            .Append("<title>").Append(encoder.Encode(app.Name)).Append("</title>\n")
            .Append("<style>\n").Append(Styles.Value).Append("</style>\n");

        if (!string.IsNullOrWhiteSpace(app.Stylesheet))
        {
            html.Append("<style>\n").Append(EscapeRawText(app.Stylesheet, "style")).Append("\n</style>\n");
        }

        html.Append("</head>\n<body>\n")
            .Append("<div id=\"mcp-status\" class=\"mcp-status\" role=\"status\" hidden></div>\n")
            .Append("<div id=\"mcp-root\" data-mcp-app=\"").Append(encoder.Encode(app.Uri)).Append("\">")
            .Append("<p class=\"mcp-razor-loading\">Loading…</p></div>\n")
            .Append("<script>\n").Append(EscapeRawText(Script.Value, "script")).Append("\n</script>\n")
            .Append("</body>\n</html>\n");

        return html.ToString();
    }

    // Raw text elements end at "</tag"; make sure the content cannot close them early.
    private static string EscapeRawText(string content, string tag) =>
        content.Replace("</" + tag, "<\\/" + tag, StringComparison.OrdinalIgnoreCase);

    internal static string ReadResource(string name)
    {
        using var stream = typeof(McpAppShell).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Embedded resource '{name}' not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
