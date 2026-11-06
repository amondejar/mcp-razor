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

- **The first view is rendered inside the tool result.** When the model calls a tool that points to an app, McpRazor
  renders the view and sends it in the result's `_meta` (`mcpRazor/view`). The app shows it right away, without a
  round trip, and it also shows in hosts that do not forward the app's tool calls to the server. If the prerender fails, the app falls back to `mcp_razor_render`.
- **The host's locale, theme and time zone reach the first view.** The prerendered view is rendered before the server
  knows them; once the host shares them, the runtime renders the view again and swaps it in (within 5 s, before the
  user can interact). If the host does not forward the call, the prerendered view stays.
- **Components run with the host's culture.** `CultureInfo.CurrentCulture` and `CurrentUICulture` are set to the
  host's locale while a view renders and handles events, so `ToString("C")` and friends follow the user's language
  without extra code. `IMcpAppHost.Culture` exposes it. When the host sends no locale, the new
  `McpRazorOptions.DefaultCulture` is used, or the server's culture (before, formatting was invariant).
- The server logs, at Information level, the locale, theme and time zone each host sends and the culture used.
- **`McpRazorOptions.Prerender`** (`WhenHostSupportsApps` by default, `Always`, `Never`). By default the first view is
  only rendered for clients that declared MCP Apps support, so text-only clients use no server memory. Over
  stateless HTTP, where the server does not know the client's capabilities, it prerenders.
- **New sample, `SalesOps`**, with two apps: a sales dashboard (KPIs, an SVG bar chart written in Razor, filters,
  month drill-down, *Explain this month*, full screen) and a price change review (the model proposes, the user
  selects, adjusts and confirms; the server applies a change set once, only to its products, never below cost).
- **Browser tests in CI** (`tests/e2e`): the LoanReview sample in the preview host, covering the review queue, the
  form, the host locale, a host that does not forward app tool calls, and three cold starts.
- The preview host accepts `?locale=` (`none` to send no locale) and `?timeZone=`.
- README: a screenshot from Claude Desktop and a host compatibility table.
- The preview host has a `?blockAppTools=1` mode that behaves like a host that does not forward app tool calls.
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
