# Changelog

## 0.1.0-preview.2

### Fixes

- **Apps did not show in Claude.** The runtime introduced itself in `ui/initialize` with `clientInfo`, as the text of
  the 2026-01-26 specification says, but host SDKs validate `appInfo`. Hosts rejected the handshake, never sent the
  tool result, and the app was never rendered. The runtime now sends `appInfo` (and `clientInfo` for older hosts).
- **Hidden iframes.** Sizes were reported before the handshake, which hosts ignore, and never again, so the iframe
  kept a height of 0 and not even errors were visible. Sizes are now reported once connected (or once the handshake
  fails), measured like the official SDK, after every render and with a timer instead of `requestAnimationFrame`,
  which browsers pause in sandboxed iframes that have no height yet.
- Requests to the host time out (15 s for the handshake, 60 s for tool calls) and show an error instead of waiting
  forever.

### Changes

- The preview host validates `ui/initialize` like host SDKs, starts iframes at height 0 and ignores sizes reported
  before the handshake, so it fails where real hosts fail.

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
