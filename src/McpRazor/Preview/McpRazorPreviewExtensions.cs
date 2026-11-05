using McpRazor.Runtime;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Microsoft.AspNetCore.Builder;

/// <summary>Maps the local MCP Apps host used to try McpRazor apps without Claude or VS Code.</summary>
public static class McpRazorPreviewExtensions
{
    /// <summary>
    /// Serves a development page that acts as an MCP Apps host: it connects to the MCP endpoint at
    /// <paramref name="mcpPath"/>, lists the tools, and shows their apps in a sandboxed iframe.
    /// Map it only in development.
    /// </summary>
    /// <example>
    /// <code>
    /// app.MapMcp("/mcp");
    /// if (app.Environment.IsDevelopment())
    /// {
    ///     app.MapMcpRazorPreview("/preview", mcpPath: "/mcp");
    /// }
    /// </code>
    /// </example>
    public static IEndpointConventionBuilder MapMcpRazorPreview(this IEndpointRouteBuilder endpoints, string pattern = "/mcp-razor/preview", string mcpPath = "/mcp")
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        if (!mcpPath.StartsWith('/') || mcpPath.Contains('"') || mcpPath.Contains('<'))
        {
            throw new ArgumentException("mcpPath must be an absolute path such as /mcp.", nameof(mcpPath));
        }

        var page = McpAppShell.ReadResource("McpRazor.Preview.preview.html").Replace("{{MCP_PATH}}", mcpPath, StringComparison.Ordinal);
        return endpoints.MapGet(pattern, () => Results.Content(page, "text/html; charset=utf-8"))
            .ExcludeFromDescription();
    }
}
