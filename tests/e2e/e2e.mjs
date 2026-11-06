// Browser tests: the LoanReview sample running in the McpRazor preview host, which validates the
// MCP Apps handshake and sizes iframes like real hosts. Each scenario starts the sample from scratch.
//
//   dotnet build
//   cd tests/e2e && npm ci && npm test
//
// Environment: CHROME_PATH (default /usr/bin/google-chrome), E2E_CONFIGURATION (default Debug).

import { spawn } from "node:child_process";
import { existsSync, mkdirSync } from "node:fs";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import puppeteer from "puppeteer-core";

const here = dirname(fileURLToPath(import.meta.url));
const repo = resolve(here, "../..");
const configuration = process.env.E2E_CONFIGURATION ?? "Debug";
const dlls = {
  LoanReview: join(repo, "samples/LoanReview/bin", configuration, "net10.0/LoanReview.dll"),
  SalesOps: join(repo, "samples/SalesOps/bin", configuration, "net10.0/SalesOps.dll"),
};
const chrome = process.env.CHROME_PATH ?? "/usr/bin/google-chrome";
const port = 5180 + Math.floor(Math.random() * 500);
const baseUrl = `http://127.0.0.1:${port}`;
const screenshots = join(here, "screenshots");

for (const dll of Object.values(dlls)) {
  if (!existsSync(dll)) {
    console.error(`Build the samples first: ${dll} not found.`);
    process.exit(2);
  }
}
mkdirSync(screenshots, { recursive: true });

// ------------------------------------------------------------------ Helpers

class Failure extends Error {}

const check = (condition, message) => {
  if (!condition) throw new Failure(message);
};

const sleep = (ms) => new Promise((done) => setTimeout(done, ms));

async function startServer(sample, env = {}) {
  const server = spawn("dotnet", [dlls[sample]], {
    env: { ...process.env, ASPNETCORE_ENVIRONMENT: "Development", ASPNETCORE_URLS: baseUrl, ...env },
    stdio: ["ignore", "pipe", "pipe"],
  });
  let output = "";
  server.stdout.on("data", (chunk) => { output += chunk; });
  server.stderr.on("data", (chunk) => { output += chunk; });

  for (let attempt = 0; attempt < 60; attempt++) {
    try {
      const response = await fetch(`${baseUrl}/preview`);
      if (response.ok) return { stop: () => stop(server), output: () => output };
    } catch {
      // Not listening yet.
    }
    if (server.exitCode !== null) break;
    await sleep(500);
  }
  stop(server);
  throw new Error(`The sample did not start:\n${output}`);
}

function stop(server) {
  if (server.exitCode === null) server.kill("SIGTERM");
}

async function openPreview(browser, query = "") {
  const page = await browser.newPage();
  await page.setViewport({ width: 1280, height: 900 });
  const errors = [];
  page.on("pageerror", (error) => errors.push(error.message));
  page.on("console", (message) => {
    if (message.type() === "error") errors.push(message.text());
  });
  await page.goto(`${baseUrl}/preview${query}`, { waitUntil: "networkidle0" });
  await page.waitForSelector(".tool[aria-pressed=true]");
  return { page, errors };
}

async function callTool(page, name, args) {
  await page.click(`.tool[data-name=${name}]`);
  if (args) await page.$eval("#args", (element, value) => { element.value = value; }, JSON.stringify(args));
  await page.click("#run");
}

async function appFrame(page, uri) {
  const handle = await page.waitForSelector(`.frame iframe[title="${uri}"]`);
  return handle.contentFrame();
}

const iframeHeight = (page) => page.$eval(".frame iframe", (element) => element.getBoundingClientRect().height);
const hostLog = (page) => page.$eval("#log", (element) => element.innerText);
const idle = (frame) => frame.waitForFunction(() => !document.getElementById("mcp-root").hasAttribute("aria-busy"));

// ------------------------------------------------------------------ Scenarios

const scenarios = {
  async "review queue: render, decide, model context, chat message, theme"(browser) {
    const { page, errors } = await openPreview(browser);
    await callTool(page, "review_loans");
    const frame = await appFrame(page, "ui://loans/review");
    await frame.waitForSelector("table tbody tr");
    check((await frame.$eval("p.muted", (e) => e.textContent)).startsWith("4 pending"), "four pending loans");
    await sleep(300);
    check((await iframeHeight(page)) > 250, "iframe sized to the table");
    await page.screenshot({ path: join(screenshots, "review.png") });

    await frame.click("tbody tr:nth-child(2) button");
    await frame.waitForSelector("textarea#note");
    await frame.type("textarea#note", "Debt ratio above 40%");
    await frame.$eval("textarea#note", (e) => e.blur());
    await idle(frame);
    await frame.click("button.danger");
    await frame.waitForSelector(".status-rejected");

    await frame.click("tbody tr:nth-child(1) button");
    await frame.waitForSelector("button.primary");
    await frame.click("button.primary");
    await frame.waitForSelector(".status-approved");

    await page.waitForFunction(() => !document.getElementById("context").classList.contains("empty"));
    const context = await page.$eval("#context", (e) => e.textContent);
    check(context.includes("L-1043") && context.includes("rejected") && context.includes("Debt ratio above 40%"), `model context: ${context}`);
    check(context.includes("L-1042") && context.includes("approved"), `model context: ${context}`);

    await frame.click(".footer button");
    await page.waitForSelector(".bubble.user");

    await page.click("#theme");
    await frame.waitForFunction(() => document.documentElement.dataset.theme);

    await page.click("#close");
    await page.waitForFunction(() => document.querySelector("#log").textContent.includes("mcp_razor_release"));
    check(errors.length === 0, `browser errors: ${errors.join("; ")}`);
  },

  async "customer form: validation and submission"(browser) {
    const { page, errors } = await openPreview(browser);
    await callTool(page, "new_customer", { name: "Ana Gil" });
    const frame = await appFrame(page, "ui://customers/new");
    await frame.waitForSelector("form");

    await frame.click("button[type=submit]");
    await frame.waitForSelector(".validation-message");
    const messages = await frame.$$eval(".validation-message", (all) => all.map((e) => e.textContent));
    check(messages.includes("The email is required."), `validation: ${messages.join(" | ")}`);
    await page.screenshot({ path: join(screenshots, "form-validation.png") });

    await frame.type("#email", "ana.gil@example.com");
    await frame.$eval("#email", (e) => e.blur());
    await frame.waitForFunction(() => document.querySelectorAll(".validation-message").length === 1);
    await idle(frame);
    await sleep(200); // let the host resize the iframe before clicking
    await frame.click("#privacy");
    await frame.waitForFunction(() => document.querySelectorAll(".validation-message").length === 0);

    await frame.focus("#name");
    await page.keyboard.press("Enter"); // implicit submission, intercepted by the runtime
    await frame.waitForFunction(() => document.querySelector("h1")?.textContent === "Customer created");
    check(errors.length === 0, `browser errors: ${errors.join("; ")}`);
  },

  async "host locale: the prerendered view is replaced by one in the host's locale"(browser) {
    const { page } = await openPreview(browser, "?locale=es-ES&timeZone=Europe/Madrid");
    await callTool(page, "review_loans");
    const frame = await appFrame(page, "ui://loans/review");
    await frame.waitForFunction(() => document.querySelector("p.muted")?.textContent.includes("88.500"));
    const summary = await frame.$eval("p.muted", (e) => e.textContent);
    check(summary.includes("88.500 €"), `summary in es-ES: ${summary}`);

    // The view the user sees is live: events go to the re-rendered view.
    await frame.click("tbody tr:nth-child(1) button");
    await frame.waitForSelector("button.primary");
    const log = await hostLog(page);
    check(log.includes("mcp_razor_render") && log.includes("mcp_razor_event"), "re-render, then event");
  },

  async "host without locale: the server's culture (es-ES)"(browser) {
    const { page } = await openPreview(browser, "?locale=none");
    await callTool(page, "review_loans");
    const frame = await appFrame(page, "ui://loans/review");
    await frame.waitForSelector("table tbody tr");
    await sleep(500);
    const summary = await frame.$eval("p.muted", (e) => e.textContent);
    check(summary.includes("88.500 €"), `summary with the server culture: ${summary}`);
  },

  async "host that does not forward app tool calls: prerendered view, clear error on click"(browser) {
    const { page } = await openPreview(browser, "?blockAppTools=1&locale=es-ES");
    await callTool(page, "review_loans");
    const frame = await appFrame(page, "ui://loans/review");
    await frame.waitForSelector("table tbody tr", { timeout: 10_000 });
    check((await frame.$eval("p.muted", (e) => e.textContent)).startsWith("4 pending"), "prerendered view shown");
    await sleep(300);
    check((await iframeHeight(page)) > 250, "iframe sized to the prerendered table");
    await page.screenshot({ path: join(screenshots, "blocked-host.png") });

    await frame.click("tbody tr:nth-child(1) button");
    await frame.waitForSelector("#mcp-status:not([hidden])", { timeout: 10_000 });
    const status = await frame.$eval("#mcp-status", (e) => e.textContent);
    check(status.includes("does not forward tool calls"), `error shown: ${status}`);
  },
};

// Cold start: the first render after the server starts is the slowest one. The iframe must still grow to fit.
async function coldStart(browser) {
  const { page } = await openPreview(browser);
  await callTool(page, "review_loans");
  const frame = await appFrame(page, "ui://loans/review");
  await frame.waitForSelector("table tbody tr");
  await sleep(1500);
  const height = await iframeHeight(page);
  check(height > 250, `iframe height after a cold start: ${height}px`);
}

for (let run = 1; run <= 3; run++) {
  scenarios[`cold start ${run}: the iframe fits the first render`] = coldStart;
}

// ------------------------------------------------------------------ Run

const browser = await puppeteer.launch({ executablePath: chrome, headless: true, args: ["--no-sandbox", "--disable-gpu"] });
let failed = 0;

// ------------------------------------------------------------------ SalesOps scenarios

const salesScenarios = {
  async "sales dashboard: KPIs, month breakdown, explain, filters, metric, full screen"(browser) {
    const { page, errors } = await openPreview(browser, "?locale=es-ES");
    await callTool(page, "sales_dashboard", { year: 2026, region: "South" });
    const frame = await appFrame(page, "ui://sales/dashboard");
    await frame.waitForSelector("svg path.bar");
    check((await frame.$$("svg path.bar")).length === 10, "ten months with data in 2026");
    const revenue = await frame.$eval(".kpi-value", (e) => e.textContent);
    check(/^\d{3}\.\d{3} €$/.test(revenue.trim()), `revenue KPI in es-ES: ${revenue}`);

    await frame.click("g.col:nth-of-type(6)");   // June
    await frame.waitForSelector(".detail");
    const stats = await frame.$eval(".detail-stats", (e) => e.textContent);
    check(stats.includes("-34,4 %"), `the June dip in the South: ${stats}`);
    check((await frame.$$(".detail tbody tr")).length === 5, "top five products");
    await sleep(300);
    await page.screenshot({ path: join(screenshots, "sales-dashboard.png") });

    await frame.click(".detail-actions .primary");
    await page.waitForSelector(".bubble.user");
    const message = await page.$eval(".bubble.user", (e) => e.textContent);
    check(message.startsWith("Explain the sales of June 2026 (South region)"), `message: ${message}`);

    await frame.select("#region", "All");
    await frame.waitForFunction(() => document.querySelector("figcaption").textContent.includes("All regions"));
    check(!(await frame.$(".detail")), "changing a filter closes the breakdown");
    const context = await page.$eval("#context", (e) => e.textContent);
    check(context.includes("2026, All regions"), `model context: ${context}`);

    await frame.click(".segmented button:nth-child(2)");
    await frame.waitForFunction(() => document.querySelector("figcaption").textContent.startsWith("Orders by month"));

    await frame.click(".dash .icon");
    await page.waitForSelector(".frame.fullscreen");
    check(errors.length === 0, `browser errors: ${errors.join("; ")}`);
  },

  async "price review: warnings, filter, below-cost guard, confirm and apply"(browser) {
    const { page, errors } = await openPreview(browser, "?locale=es-ES");
    await callTool(page, "propose_price_changes", { category: "Electronics", percent: -18, reason: "Black Friday: match competitors on electronics for one week." });
    const frame = await appFrame(page, "ui://pricing/review");
    await frame.waitForSelector("table tbody tr");
    const summary = await frame.$eval(".summary", (e) => e.textContent);
    check(summary.includes("6 of 8 selected") && summary.includes("2 with warnings"), `summary: ${summary}`);
    check(!(await frame.$eval("#sel-ELE-403", (e) => e.checked)), "the smart plug (margin warning) starts unselected");
    await sleep(300);
    await page.screenshot({ path: join(screenshots, "price-review.png") });

    await frame.type("#filter", "keyboard");
    await frame.waitForFunction(() => document.querySelectorAll("tbody tr").length === 1);
    await frame.click("#filter", { count: 3 });
    await page.keyboard.press("Backspace");
    await frame.waitForFunction(() => document.querySelectorAll("tbody tr").length === 8);

    // A price below cost: the row is flagged and cannot be selected.
    await frame.click("#price-ELE-404", { count: 3 });
    await frame.type("#price-ELE-404", "15");
    await frame.$eval("#price-ELE-404", (e) => e.blur());
    await frame.waitForFunction(() => document.querySelector("#sel-ELE-404").disabled);
    const badges = await frame.$eval("#price-ELE-404", (e) => e.closest("tr").textContent);
    check(badges.includes("Below cost"), "below-cost badge");
    check((await frame.$eval(".summary", (e) => e.textContent)).includes("5 of 8 selected"), "the row was deselected");

    await idle(frame);
    await frame.click("#apply");
    await frame.waitForSelector("#confirm");
    await frame.click("#confirm");
    await frame.waitForSelector(".outcome-applied");
    const outcome = await frame.$eval(".outcome-applied", (e) => e.textContent);
    check(outcome.includes("Applied 5 changes") && outcome.includes("skipped 3"), `outcome: ${outcome}`);
    check(!(await frame.$("#apply")) && !(await frame.$("input.price")), "read-only after applying");

    await page.waitForFunction(() => !document.getElementById("context").classList.contains("empty"));
    const context = await page.$eval("#context", (e) => e.textContent);
    check(context.includes("applied price change CS-") && context.includes("ELE-404") && context.includes("Below cost"), `model context: ${context}`);
    check(errors.length === 0, `browser errors: ${errors.join("; ")}`);
  },

  async "price review: discard"(browser) {
    const { page } = await openPreview(browser);
    await callTool(page, "propose_price_changes", { category: "Garden", percent: 5, reason: "Spring season." });
    const frame = await appFrame(page, "ui://pricing/review");
    await frame.waitForSelector("table tbody tr");
    await frame.click("button.danger");
    await frame.waitForSelector(".outcome-discarded");
    const context = await page.$eval("#context", (e) => e.textContent);
    check(context.includes("discarded") && context.includes("Garden"), `model context: ${context}`);
  },
};

// Scenarios that need the server in a given culture (.NET reads it from LANG on Linux and macOS).
const serverEnvironment = {
  "host without locale: the server's culture (es-ES)": { LANG: "es_ES.UTF-8", LC_ALL: "es_ES.UTF-8" },
};

const runs = [
  ...Object.entries(scenarios).map(([name, scenario]) => ["LoanReview", name, scenario]),
  ...Object.entries(salesScenarios).map(([name, scenario]) => ["SalesOps", name, scenario]),
];

for (const [sample, name, scenario] of runs) {
  const server = await startServer(sample, serverEnvironment[name]);
  const started = Date.now();
  try {
    await scenario(browser);
    console.log(`✓ ${name} (${Date.now() - started} ms)`);
  } catch (error) {
    failed++;
    console.log(`✗ ${name}\n    ${error.message.replaceAll("\n", "\n    ")}`);
    if (!(error instanceof Failure)) console.log(server.output().split("\n").slice(-20).join("\n"));
  } finally {
    for (const page of await browser.pages()) await page.close();
    server.stop();
    await sleep(300);
  }
}

await browser.close();
console.log(`\n${runs.length - failed} passed, ${failed} failed`);
process.exit(failed === 0 ? 0 : 1);
