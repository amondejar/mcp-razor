/*! McpRazor runtime — MCP Apps (SEP-1865, 2026-01-26) client for Razor components rendered on the server. MIT. */
(() => {
  "use strict";

  const PROTOCOL_VERSION = "2026-01-26";
  const RUNTIME_VERSION = "0.1.1";
  const TOOL_RENDER = "mcp_razor_render";
  const TOOL_EVENT = "mcp_razor_event";
  const TOOL_RELEASE = "mcp_razor_release";

  const root = document.getElementById("mcp-root");
  const status = document.getElementById("mcp-status");
  const appUri = root.getAttribute("data-mcp-app");

  let nextId = 1;
  const pending = new Map();
  let hostContext = {};
  let toolInput = null;
  let viewId = null;
  let queue = Promise.resolve();
  const serverValues = new WeakMap();

  // ---------------------------------------------------------------- JSON-RPC over postMessage

  const post = (message) => window.parent.postMessage(message, "*");

  // A host that never answers must not leave the app hanging silently.
  const TIMEOUTS = { "ui/initialize": 15000, "tools/call": 60000 };
  const DEFAULT_TIMEOUT = 30000;

  const request = (method, params) =>
    new Promise((resolve, reject) => {
      const id = nextId++;
      const timer = setTimeout(() => {
        if (pending.delete(id)) reject(new Error(`The host did not answer ${method}.`));
      }, TIMEOUTS[method] ?? DEFAULT_TIMEOUT);
      pending.set(id, {
        resolve: (value) => { clearTimeout(timer); resolve(value); },
        reject: (error) => { clearTimeout(timer); reject(error); },
      });
      post({ jsonrpc: "2.0", id, method, params: params ?? {} });
    });

  const notify = (method, params) => post({ jsonrpc: "2.0", method, params: params ?? {} });
  const respond = (id, result) => post({ jsonrpc: "2.0", id, result: result ?? {} });

  window.addEventListener("message", (event) => {
    if (event.source !== window.parent) return;
    const message = event.data;
    if (!message || message.jsonrpc !== "2.0") return;

    if (message.method === undefined) {
      const call = pending.get(message.id);
      if (!call) return;
      pending.delete(message.id);
      if (message.error) call.reject(new Error(message.error.message || "The host returned an error."));
      else call.resolve(message.result);
      return;
    }

    handle(message);
  });

  function handle(message) {
    const params = message.params || {};
    switch (message.method) {
      case "ui/notifications/tool-input":
        toolInput = params.arguments ?? null;
        break;
      case "ui/notifications/tool-result":
        enqueue(() => onToolResult(params));
        break;
      case "ui/notifications/tool-cancelled":
        showStatus("info", params.reason ? `Cancelled: ${params.reason}` : "The tool call was cancelled.");
        break;
      case "ui/notifications/host-context-changed":
        hostContext = { ...hostContext, ...params };
        applyHostContext();
        break;
      case "ui/resource-teardown":
        teardown().finally(() => respond(message.id));
        break;
      case "ping":
        if (message.id !== undefined) respond(message.id);
        break;
      default:
        if (message.id !== undefined) {
          post({ jsonrpc: "2.0", id: message.id, error: { code: -32601, message: `Method not found: ${message.method}` } });
        }
    }
  }

  // ---------------------------------------------------------------- Server calls

  function enqueue(work) {
    queue = queue.then(work, work);
    return queue;
  }

  async function callTool(name, args) {
    const result = await request("tools/call", { name, arguments: args });
    if (!result || result.isError) throw new Error(textOf(result) || "The server returned an error.");
    const envelope = result.structuredContent ?? parseJson(textOf(result));
    if (!envelope || envelope.mcpRazor !== 1) throw new Error("Unexpected answer from the server.");
    return envelope;
  }

  async function onToolResult(result) {
    // When the app calls one of our tools, some hosts also forward that result here: ignore it.
    if (result.structuredContent && result.structuredContent.mcpRazor === 1) return;

    if (result.isError) {
      showStatus("error", textOf(result) || "The tool failed.");
      return;
    }

    const previous = viewId;
    try {
      const envelope = await callTool(TOOL_RENDER, {
        uri: appUri,
        data: result.structuredContent ?? null,
        input: toolInput,
        hostContext: contextForServer(),
      });
      await apply(envelope);
      if (previous && previous !== viewId) callTool(TOOL_RELEASE, { viewId: previous }).catch(() => {});
    } catch (error) {
      showStatus("error", error.message);
    }
  }

  async function sendEvent(handler, type, payload) {
    if (!viewId) return;
    root.setAttribute("aria-busy", "true");
    try {
      const envelope = await callTool(TOOL_EVENT, {
        viewId,
        handler: Number(handler),
        type,
        payload,
        hostContext: contextForServer(),
      });
      await apply(envelope);
    } catch (error) {
      showStatus("error", error.message);
    } finally {
      root.removeAttribute("aria-busy");
    }
  }

  async function apply(envelope) {
    viewId = envelope.viewId;
    clearStatus();
    morph(root, envelope.html || "");
    reportSize();
    await runEffects(envelope.effects);
  }

  async function runEffects(effects) {
    for (const effect of effects || []) {
      try {
        switch (effect.type) {
          case "update-model-context": {
            const params = { content: [{ type: "text", text: effect.text }] };
            if (effect.structuredContent !== undefined) params.structuredContent = effect.structuredContent;
            await request("ui/update-model-context", params);
            break;
          }
          case "message":
            await request("ui/message", { role: "user", content: { type: "text", text: effect.text } });
            break;
          case "open-link":
            await request("ui/open-link", { url: effect.url });
            break;
          case "display-mode":
            await request("ui/request-display-mode", { mode: effect.mode });
            break;
        }
      } catch (error) {
        console.warn("[mcp-razor]", effect.type, error);
      }
    }
  }

  async function teardown() {
    const current = viewId;
    viewId = null;
    if (current) {
      try {
        await callTool(TOOL_RELEASE, { viewId: current });
      } catch {
        // The view expires on the server anyway.
      }
    }
  }

  // ---------------------------------------------------------------- DOM events

  // DOM event -> Blazor event names it can trigger (focus and blur do not bubble, so their bubbling twins are used).
  const EVENTS = {
    click: ["click"],
    dblclick: ["dblclick"],
    change: ["change"],
    input: ["input"],
    submit: ["submit"],
    keydown: ["keydown"],
    keyup: ["keyup"],
    focusin: ["focusin", "focus"],
    focusout: ["focusout", "blur"],
  };

  const inputTimers = new Map();

  for (const domType of Object.keys(EVENTS)) {
    root.addEventListener(domType, onDomEvent);
  }

  function onDomEvent(event) {
    if (event.type === "submit") event.preventDefault();
    if (!(event.target instanceof Element)) return;

    // Hosts sandbox the app without "allow-forms", and then browsers block a form submission before the
    // "submit" event fires. So a submit button click, or Enter in a field, is turned into the submit event here.
    const form = submittedForm(event);
    if (form) {
      event.preventDefault();
      if (event.type === "keydown") flushChange(event.target);
      enqueue(() => sendEvent(form.getAttribute("data-mcp-on-submit"), "submit", {}));
      return;
    }

    for (const name of EVENTS[event.type]) {
      const element = event.target.closest(`[data-mcp-on-${name}]`);
      if (!element || !root.contains(element)) continue;

      if (element.hasAttribute(`data-mcp-pd-${name}`)) event.preventDefault();
      const handler = element.getAttribute(`data-mcp-on-${name}`);
      const payload = payloadFor(event, element, name);

      if (name === "input") {
        // Coalesce keystrokes: send the latest value once the user pauses.
        clearTimeout(inputTimers.get(element));
        inputTimers.set(element, setTimeout(() => {
          inputTimers.delete(element);
          enqueue(() => sendEvent(handler, name, payloadFor(event, element, name)));
        }, 200));
      } else {
        enqueue(() => sendEvent(handler, name, payload));
      }
      return;
    }
  }

  function submittedForm(event) {
    const target = event.target;
    let form = null;

    if (event.type === "click") {
      const submitter = target.closest("button, input[type=submit], input[type=image]");
      const isSubmit = submitter && (submitter instanceof HTMLButtonElement ? submitter.type === "submit" : true);
      if (isSubmit && !submitter.disabled) form = submitter.form;
    } else if (event.type === "keydown" && event.key === "Enter" && !event.isComposing && target instanceof HTMLInputElement) {
      const implicit = !["button", "submit", "reset", "checkbox", "radio", "file", "image"].includes(target.type);
      if (implicit) form = target.form;
    }

    return form && root.contains(form) && form.hasAttribute("data-mcp-on-submit") ? form : null;
  }

  // Enter does not fire "change" when the submission is intercepted: send the field's value first.
  function flushChange(element) {
    const handler = element.getAttribute("data-mcp-on-change");
    if (handler && element.value !== serverValues.get(element)) {
      const payload = payloadFor(new Event("change"), element, "change");
      enqueue(() => sendEvent(handler, "change", payload));
    }
  }

  function payloadFor(event, element, name) {
    const payload = {};
    if (name === "change" || name === "input") {
      if (element instanceof HTMLInputElement && (element.type === "checkbox" || element.type === "radio")) {
        payload.value = element.type === "checkbox" ? element.checked : element.value;
      } else if (element instanceof HTMLSelectElement && element.multiple) {
        payload.value = Array.from(element.selectedOptions, (option) => option.value);
      } else {
        payload.value = element.value;
      }
    }
    if (event instanceof MouseEvent) {
      Object.assign(payload, { detail: event.detail, button: event.button });
    }
    if (event instanceof KeyboardEvent) {
      Object.assign(payload, { key: event.key, code: event.code, repeat: event.repeat });
    }
    if (event instanceof MouseEvent || event instanceof KeyboardEvent) {
      Object.assign(payload, { shiftKey: event.shiftKey, ctrlKey: event.ctrlKey, altKey: event.altKey, metaKey: event.metaKey });
    }
    return payload;
  }

  // ---------------------------------------------------------------- DOM morphing

  function morph(target, html) {
    const template = document.createElement("template");
    template.innerHTML = html;
    morphChildren(target, template.content);
  }

  function morphChildren(oldParent, newParent) {
    const oldNodes = Array.from(oldParent.childNodes);
    const newNodes = Array.from(newParent.childNodes);

    newNodes.forEach((newNode, index) => {
      const oldNode = oldNodes[index];
      if (!oldNode) {
        oldParent.appendChild(newNode);
      } else if (!sameKind(oldNode, newNode)) {
        oldParent.replaceChild(newNode, oldNode);
      } else if (oldNode.nodeType === Node.ELEMENT_NODE) {
        morphElement(oldNode, newNode);
      } else if (oldNode.nodeValue !== newNode.nodeValue) {
        oldNode.nodeValue = newNode.nodeValue;
      }
    });

    for (let index = newNodes.length; index < oldNodes.length; index++) {
      oldNodes[index].remove();
    }
  }

  function sameKind(a, b) {
    if (a.nodeType !== b.nodeType) return false;
    if (a.nodeType !== Node.ELEMENT_NODE) return true;
    return a.tagName === b.tagName && (a.getAttribute("id") || "") === (b.getAttribute("id") || "");
  }

  function morphElement(oldElement, newElement) {
    for (const attribute of Array.from(oldElement.attributes)) {
      if (!newElement.hasAttribute(attribute.name)) oldElement.removeAttribute(attribute.name);
    }
    for (const attribute of Array.from(newElement.attributes)) {
      if (oldElement.getAttribute(attribute.name) !== attribute.value) oldElement.setAttribute(attribute.name, attribute.value);
    }

    if (oldElement instanceof HTMLInputElement) {
      if (oldElement.type === "checkbox" || oldElement.type === "radio") {
        oldElement.checked = newElement.hasAttribute("checked");
      } else {
        syncValue(oldElement, newElement.getAttribute("value") ?? "");
      }
      return;
    }

    if (oldElement instanceof HTMLTextAreaElement) {
      syncValue(oldElement, newElement.defaultValue);
      return;
    }

    morphChildren(oldElement, newElement);

    if (oldElement instanceof HTMLSelectElement) {
      Array.from(oldElement.options).forEach((option, index) => {
        option.selected = !!newElement.options[index]?.hasAttribute("selected");
      });
    }
  }

  // Keeps what the user is typing unless the server changed the value on purpose.
  function syncValue(element, value) {
    const focused = document.activeElement === element;
    if (!focused || serverValues.get(element) !== value) {
      if (element.value !== value) element.value = value;
    }
    serverValues.set(element, value);
  }

  // ---------------------------------------------------------------- Host context, size and status

  function contextForServer() {
    const { theme, locale, timeZone, displayMode, platform } = hostContext;
    return { theme, locale, timeZone, displayMode, platform };
  }

  function applyHostContext() {
    const html = document.documentElement;
    if (hostContext.theme === "light" || hostContext.theme === "dark") {
      html.setAttribute("data-theme", hostContext.theme);
      html.style.colorScheme = hostContext.theme;
    }

    const variables = hostContext.styles?.variables || {};
    for (const [name, value] of Object.entries(variables)) {
      if (name.startsWith("--") && typeof value === "string") html.style.setProperty(name, value);
    }

    const fonts = hostContext.styles?.css?.fonts;
    if (typeof fonts === "string" && fonts) {
      let style = document.getElementById("mcp-host-fonts");
      if (!style) {
        style = document.createElement("style");
        style.id = "mcp-host-fonts";
        document.head.appendChild(style);
      }
      style.textContent = fonts;
    }

    const dimensions = hostContext.containerDimensions || {};
    html.style.height = "height" in dimensions ? "100vh" : "";
    html.style.maxHeight = dimensions.maxHeight ? `${dimensions.maxHeight}px` : "";
    html.style.width = "width" in dimensions ? "100vw" : "";
    html.style.maxWidth = dimensions.maxWidth ? `${dimensions.maxWidth}px` : "";
  }

  // Hosts ignore size notifications sent before the handshake, and keep the iframe at height 0 until
  // they get one. So sizes are reported only once connected (or once the handshake failed, so the
  // error can be seen), measuring the document the same way as the official SDK.
  let sizeReporting = false;
  let sizeScheduled = false;
  let lastSize = "";

  // A timer instead of requestAnimationFrame: browsers pause animation frames in sandboxed iframes
  // that are hidden or have no height yet, which is exactly when the size must be reported.
  const reportSize = () => {
    if (!sizeReporting || sizeScheduled) return;
    sizeScheduled = true;
    setTimeout(() => {
      sizeScheduled = false;
      const html = document.documentElement;
      const originalHeight = html.style.height;
      html.style.height = "max-content";
      const height = Math.ceil(html.getBoundingClientRect().height);
      html.style.height = originalHeight;
      const width = Math.ceil(window.innerWidth);
      const key = `${width}x${height}`;
      if (key !== lastSize) {
        lastSize = key;
        notify("ui/notifications/size-changed", { width, height });
      }
    }, 16);
  };

  function startSizeReporting() {
    if (sizeReporting) return;
    sizeReporting = true;
    const observer = new ResizeObserver(reportSize);
    observer.observe(document.documentElement);
    observer.observe(document.body);
    reportSize();
  }

  function showStatus(kind, text) {
    status.hidden = false;
    status.className = `mcp-status mcp-status-${kind}`;
    status.textContent = text;
    reportSize();
  }

  function clearStatus() {
    status.hidden = true;
    status.textContent = "";
  }

  function textOf(result) {
    const block = result?.content?.find?.((item) => item.type === "text");
    return block ? block.text : "";
  }

  function parseJson(text) {
    try {
      return JSON.parse(text);
    } catch {
      return null;
    }
  }

  // ---------------------------------------------------------------- Start

  (async () => {
    try {
      const appInfo = { name: "McpRazor", version: RUNTIME_VERSION };
      const result = await request("ui/initialize", {
        // "appInfo" is what host SDKs validate; "clientInfo" is the name in the text of the 2026-01-26
        // specification, kept for hosts that followed it.
        appInfo,
        clientInfo: appInfo,
        appCapabilities: { availableDisplayModes: ["inline", "fullscreen"] },
        protocolVersion: PROTOCOL_VERSION,
      });
      hostContext = result?.hostContext || {};
      applyHostContext();
      notify("ui/notifications/initialized", {});
      startSizeReporting();
    } catch (error) {
      showStatus("error", `Could not connect to the host: ${error.message}`);
      startSizeReporting();
    }
  })();
})();
