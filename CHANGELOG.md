# Changelog

## 0.1.0-preview.1

First preview.

- `WithRazorApps` registers Razor components as MCP Apps on an MCP server: one `ui://` resource per app
  (`text/html;profile=mcp-app`) and three app-only tools (`mcp_razor_render`, `mcp_razor_event`, `mcp_razor_release`).
- `[McpApp]` attribute and `McpAppComponent<TData>`, whose `Data` receives the tool's `structuredContent`.
- Server-side renderer that keeps live components and exposes their event handlers to the app: `@onclick`, `@bind`,
  `EditForm` and validation, child components, async lifecycle and handlers.
- `IMcpAppHost`: host context and tool input; `UpdateModelContext`, `SendMessage`, `OpenLink` and `RequestDisplayMode`.
- Runtime for the app (MCP Apps `2026-01-26`): handshake, tool input and result, DOM patching that keeps focus and
  typed values, size notifications, host theme variables, form submission inside sandboxed iframes, and teardown.
- Views with their own DI scope, idle timeout, capacity limit, session binding and handler validation.
- `MapMcpRazorPreview`: a local MCP Apps host for development.
- Sample `LoanReview` (HTTP and stdio) with a review queue and a customer form.
