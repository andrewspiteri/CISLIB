// Actual desktop UI on the ordinary production host. Controlled responses below test only browser race/paging states.
import assert from "node:assert/strict";
import { mkdir, writeFile } from "node:fs/promises";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { randomUUID } from "node:crypto";
import { chromium, expect } from "@playwright/test";

export async function verifyBrowser({ origin, applicationA, applicationB, registrationA, operatorAContext, operatorBContext }) {
  const root = resolve(dirname(fileURLToPath(import.meta.url)), "../../../../..");
  const evidence = join(root, ".artifacts/management-portal/CIS-0029/WORK-005", "browser-" + randomUUID());
  await mkdir(evidence, { recursive: true });
  const browser = await chromium.launch({ channel: "msedge", headless: true });
  const contexts = [];
  const cases = [];
  const errors = [];
  let original;
  let stage = "launch";
  let activePage;
  async function context(api) {
    const result = await browser.newContext({ storageState: await api.storageState(), viewport: { width: 1600, height: 1000 },
      // Browser-only trust exception for this newly generated test root. All server transports validate the explicit root.
      ignoreHTTPSErrors: true });
    result.on("page", page => page.on("pageerror", error => errors.push(error.name + ": " + error.message.slice(0, 256))));
    contexts.push(result);
    return result;
  }
  async function screenshot(page, name) { await page.screenshot({ path: join(evidence, name + ".png"), fullPage: true }); }
  try {
    stage = "applications-filtering";
    const a = await context(operatorAContext);
    const page = await a.newPage();
    activePage = page;
    await page.goto(origin + "/applications");
    await expect(page.getByRole("heading", { name: "Applications", exact: true })).toBeVisible();
    await expect(page.getByRole("link", { name: "managed-a", exact: true })).toBeVisible();
    await expect(page.getByRole("link", { name: "managed-b", exact: true })).toHaveCount(0);
    await expect(page.getByLabel("Environment", { exact: true }).getByRole("option", { name: "Development", exact: true })).toHaveCount(1);
    await screenshot(page, "applications-native");
    await page.getByLabel("Environment", { exact: true }).selectOption("Development");
    await page.getByLabel("Application name or identity").fill("no-such-application");
    await page.getByRole("button", { name: "Apply filters" }).click();
    await expect(page.getByRole("heading", { name: "No matching applications" })).toBeVisible();
    await page.getByRole("button", { name: "Clear filters", exact: true }).click();
    await expect(page.getByLabel("Application name or identity")).toHaveValue("");
    await expect(page.getByLabel("Environment", { exact: true })).toHaveValue("");
    await expect(page.getByRole("link", { name: "managed-a", exact: true })).toBeVisible();
    cases.push("native-clear-filters");
    await page.getByLabel("Application name or identity").fill("managed-a");
    await page.getByLabel("Environment", { exact: true }).selectOption("Development");
    await page.getByRole("button", { name: "Apply filters" }).click();
    await page.getByRole("link", { name: "managed-a", exact: true }).click();
    stage = "instances-and-panel-keyboard";
    await expect(page.getByRole("tab", { name: "Instances", exact: true })).toHaveAttribute("aria-selected", "true");
    const instance = page.getByRole("button", { name: "seed-a", exact: true });
    await expect(instance).toBeVisible();
    await expect(page.getByRole("row").filter({ has: instance })).toContainText("1.0.0");
    const second = page.getByRole("button", { name: "seed-a-second", exact: true });
    await expect(second).toBeVisible();
    await expect(page.getByRole("row").filter({ has: second })).toContainText("2.4.2");
    await screenshot(page, "instances-native");
    await instance.focus();
    await page.keyboard.press("Enter");
    const panel = page.getByRole("dialog");
    await expect(panel.getByRole("heading", { name: "seed-a", exact: true })).toBeVisible();
    await expect(panel).toContainText("Reported app version");
    await screenshot(page, "instance-panel-native");
    await page.keyboard.press("Escape");
    await expect(panel).toHaveCount(0);
    await expect(instance).toBeFocused();
    cases.push("native-filter-environment-empty", "native-per-registration-versions", "keyboard-panel-escape-focus-return");

    stage = "configuration-and-capabilities";
    await page.getByRole("tab", { name: "Configuration", exact: true }).click();
    await expect(page.getByRole("heading", { name: "Configuration list", exact: true })).toBeVisible();
    const configuration = page.getByRole("button", { name: "shared.settings", exact: true });
    await expect(configuration).toBeVisible();
    await screenshot(page, "configuration-native");
    await configuration.click();
    await expect(panel).toContainText("FirstHost");
    await expect(panel).toContainText("Metadata only");
    await expect(page.getByText("PORTAL_PRIVATE_DEFAULT", { exact: false })).toHaveCount(0);
    const definitions = await page.request.get(origin + `/api/portal/applications/${applicationA}/configuration`);
    const definitionText = await definitions.text();
    assert.equal(definitions.status(), 200);
    assert.ok(!definitionText.includes("PORTAL_PRIVATE_DEFAULT") && !definitionText.includes("defaultsJson"));
    await screenshot(page, "configuration-panel-native");
    await page.getByRole("button", { name: "Close details" }).click();
    await page.getByRole("tab", { name: "Capabilities", exact: true }).click();
    await expect(page.locator("tbody tr").first()).toBeVisible();
    await page.locator("tbody tr .item-link").first().click();
    await expect(panel).toContainText("Permission hints are advisory");
    await screenshot(page, "capability-panel-native");
    await page.getByRole("button", { name: "Close details" }).click();
    await page.getByRole("tab", { name: "Scheduled jobs", exact: true }).click();
    stage = "jobs-panel-and-full-details";
    const job = page.getByRole("button", { name: "Customer dormancy", exact: true });
    await expect(job).toBeVisible();
    await expect(page.getByRole("row").filter({ has: job })).toContainText("partition-eu");
    await screenshot(page, "jobs-native");
    await job.click();
    await expect(panel.getByRole("heading", { name: "Customer dormancy", exact: true })).toBeVisible();
    await expect(panel.locator(".fact").filter({ hasText: "Next occurrence" })).toContainText("Not reported");
    await screenshot(page, "job-panel-native");
    await panel.getByRole("link", { name: "View full details" }).click();
    await expect(page).toHaveURL(url => url.pathname === `/applications/${applicationA}/jobs`);
    await expect(page.getByRole("tab", { name: "Overview", exact: true })).toHaveAttribute("aria-selected", "true");
    const full = new URL(page.url());
    assert.equal(full.pathname, `/applications/${applicationA}/jobs`);
    for (const [key, expected] of Object.entries({ registration: registrationA, report: "1", scope: "customer-service", job: "customer.dormancy",
      resource: "partition-eu", mode: "scheduled", fromQuery: "managed-a", fromEnvironment: "Development" })) {
      assert.equal(full.searchParams.get(key), expected, `Full details preserves ${key}.`);
    }
    await expect(page.getByRole("tab", { name: "Overview", exact: true })).toHaveAttribute("aria-selected", "true");
    await expect(page.getByRole("heading", { name: "Job definition", exact: true })).toBeVisible();
    await expect(page.locator(".report-facts")).toContainText(registrationA);
    await expect(page.locator(".report-facts .fact").filter({ hasText: "Reported app version" })).toContainText("1.0.0");
    await expect(page.getByRole("link", { name: "Scheduled task details" })).toBeInViewport({ ratio: 1 });
    await screenshot(page, "job-overview-native");
    await page.getByRole("tab", { name: "Configuration", exact: true }).click();
    await expect(page.getByRole("heading", { name: "Reported local configuration" })).toBeVisible();
    await expect(page.locator(".fact").filter({ has: page.getByText("Local revision", { exact: true }) })).toContainText("0");
    await screenshot(page, "job-configuration-native");
    await page.getByRole("tab", { name: "Run history", exact: true }).click();
    await expect(page.getByText("This service has no run-history API.", { exact: false })).toBeVisible();
    await screenshot(page, "job-history-unavailable");
    await page.getByRole("link", { name: "Scheduled task details" }).click();
    await expect(page.getByRole("tablist", { name: "Scheduled task configuration" }).getByRole("tab")).toHaveCount(2);
    await expect(page.getByRole("tab", { name: "Trigger", exact: true })).toBeVisible();
    await page.getByRole("tab", { name: "Job", exact: true }).click();
    await expect(page.getByRole("heading", { name: "Task job configuration" })).toBeVisible();
    await page.getByRole("button", { name: "View run history" }).click();
    await expect(page.getByText("The service has no task-history API.", { exact: false })).toBeVisible();
    await screenshot(page, "task-history-unavailable");
    await page.getByRole("link", { name: "← Job details", exact: true }).click();
    await page.getByRole("link", { name: "Event details" }).click();
    await expect(page.getByText("No immutable event record", { exact: false })).toBeVisible();
    await screenshot(page, "event-unavailable");
    await page.getByRole("link", { name: "← Back to scheduled jobs", exact: true }).click();
    await expect(page.getByRole("tab", { name: "Scheduled jobs", exact: true })).toHaveAttribute("aria-selected", "true");
    await expect(job).toBeVisible();
    cases.push("native-metadata-only-configuration", "native-capability-hints", "native-whole-inventory-and-exact-report-navigation", "native-reported-local-configuration", "truthful-history-task-event-unavailable");

    stage = "independent-browser-session-target-isolation";
    const b = await context(operatorBContext);
    const pageB = await b.newPage();
    activePage = pageB;
    await pageB.goto(origin + "/applications");
    await expect(pageB.getByRole("link", { name: "managed-b", exact: true })).toBeVisible();
    await expect(pageB.getByRole("link", { name: "managed-a", exact: true })).toHaveCount(0);
    activePage = page;
    await page.goto(origin + `/applications/${applicationB}`);
    await expect(page.getByRole("main").getByRole("alert")).toContainText(/Access unavailable|Item unavailable/u);
    await expect(page.getByRole("tablist")).toHaveCount(0);
    cases.push("actual-independent-browser-session-target-isolation");

    // Controlled browser responses: exercise races and UI recovery without a production mock mode.
    stage = "controlled-delayed-old-tab-response";
    await page.goto(origin + `/applications/${applicationA}`);
    await expect(instance).toBeVisible();
    let release;
    let entered;
    let settle;
    let routeEntered = false;
    let routeFailure;
    const held = new Promise(resolve => { release = resolve; });
    const started = new Promise(resolve => { entered = resolve; });
    const settled = new Promise(resolve => { settle = resolve; });
    const configurationRoute = `**/api/portal/applications/${applicationA}/configuration*`;
    await page.route(configurationRoute, async route => {
      routeEntered = true;
      entered();
      try {
        await held;
        await route.fulfill({ json: { state: "ready", value: { items: [{ kind: "application", configurationId: "late-stale-record", schemaVersion: "1", sourceId: "source", applicationId: applicationA, environment: "Development", bindingId: "binding", hostApplicationName: "host" }] } } });
      } catch (error) {
        if (route.request().failure()?.errorText !== "net::ERR_ABORTED") { routeFailure = error; }
      } finally { settle(); }
    });
    try {
      await page.getByRole("tab", { name: "Configuration", exact: true }).click();
      await deadline(started, "The controlled old-tab request must enter.");
      await page.getByRole("tab", { name: "Capabilities", exact: true }).click();
      await expect(page.locator("tbody tr").first()).toBeVisible();
    } finally {
      release();
      if (routeEntered) { await deadline(settled, "The controlled old-tab response must settle."); }
      await page.unroute(configurationRoute);
    }
    if (routeFailure !== undefined) { throw routeFailure; }
    await page.evaluate(() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))));
    await expect(page.getByRole("tab", { name: "Capabilities", exact: true })).toHaveAttribute("aria-selected", "true");
    await expect(page.locator("tbody tr").first()).toBeVisible();
    await expect(page.getByText("late-stale-record")).toHaveCount(0);
    cases.push("controlled-delayed-old-tab-response-discarded");

    stage = "controlled-child-session-expiry";
    await page.route(`**/api/portal/applications/${applicationA}/instances*`, route => route.fulfill({ status: 401, json: { state: "unauthenticated", message: "The session expired." } }));
    await page.getByRole("tab", { name: "Instances", exact: true }).click();
    await expect(page.getByRole("main").getByRole("alert")).toContainText("Sign in required");
    await expect(page.getByRole("main").getByRole("alert").getByRole("link", { name: "Sign in", exact: true })).toBeVisible();
    await expect(page.getByRole("tablist")).toHaveCount(0);
    await expect(page.getByText("seed-a", { exact: true })).toHaveCount(0);
    cases.push("controlled-child-session-expiry-clears-content-and-explains-reauthentication");
    await screenshot(page, "child-session-expired");
    await page.unrouteAll({ behavior: "wait" });

    stage = "controlled-paging-filter-restart";
    const cursor = "controlled-next-page";
    const requestedPages = [];
    await page.route("**/api/portal/applications?*", route => {
      const url = new URL(route.request().url());
      requestedPages.push({ query: url.searchParams.get("query"), cursor: url.searchParams.get("cursor") });
      return route.fulfill({ json: { state: "ready", value: { items: [{ applicationId: url.searchParams.has("cursor") ? applicationB : applicationA,
        application: url.searchParams.has("cursor") ? "page-two" : "page-one", environment: "Development", metadata: { displayName: url.searchParams.has("cursor") ? "Page two" : "Page one" }, metadataRevision: "1" }], ...(url.searchParams.has("cursor") ? {} : { nextCursor: cursor }) } } });
    });
    // Explicit filter guarantees a query string while retaining the ordinary page-size/API boundary.
    await page.goto(origin + "/applications?query=page");
    await expect(page.getByRole("link", { name: "Page one", exact: true })).toBeVisible();
    const pager = page.getByRole("navigation", { name: "1 applications on this page pagination" });
    await pager.getByRole("button", { name: "Next", exact: true }).click();
    await expect(page.getByRole("link", { name: "Page two", exact: true })).toBeVisible();
    await pager.getByRole("button", { name: "Previous", exact: true }).click();
    await expect(page.getByRole("link", { name: "Page one", exact: true })).toBeVisible();
    await pager.getByRole("button", { name: "Next", exact: true }).click();
    await expect(page.getByRole("link", { name: "Page two", exact: true })).toBeVisible();
    await page.getByLabel("Application name or identity").fill("new-filter");
    await page.getByRole("button", { name: "Apply filters" }).click();
    await expect(page.getByRole("link", { name: "Page one", exact: true })).toBeVisible();
    await expect(pager.getByRole("button", { name: "Previous", exact: true })).toBeDisabled();
    assert.deepEqual(requestedPages.at(-1), { query: "new-filter", cursor: null });
    assert.ok(requestedPages.some(value => value.query === "page" && value.cursor === cursor));
    cases.push("controlled-bounded-next-previous-filter-restart");
    await page.unrouteAll({ behavior: "wait" });
    assert.deepEqual(errors, [], "The connected desktop must have no browser runtime errors.");
    await writeFile(join(evidence, "browser-evidence.json"), JSON.stringify({ viewport: "1600x1000", installedBrowser: "msedge", productionHost: true,
      actualNativeCases: cases.filter(value => !value.startsWith("controlled-")), controlledBrowserCases: cases.filter(value => value.startsWith("controlled-")), screenshotsContainOnlyOwnedSyntheticFixtureData: true }, null, 2));
  } catch (error) {
    original = error;
    let failureScreenshotSaved = false;
    if (activePage !== undefined) {
      try { await screenshot(activePage, "failure-" + stage); failureScreenshotSaved = true; }
      catch { /* The original assertion and all native cleanup remain required. */ }
    }
    const sourceLine = error instanceof Error ? /native-browser-checks\.mjs:(\d+):(\d+)/u.exec(error.stack ?? "") : null;
    // Source coordinates and fixed stages identify the failed assertion without persisting DOM, URLs or credentials.
    await writeFile(join(evidence, "browser-failure.json"), JSON.stringify({ stage, completedCases: cases,
      sourceLine: sourceLine === null ? null : Number(sourceLine[1]), sourceColumn: sourceLine === null ? null : Number(sourceLine[2]),
      errorKind: error instanceof Error && ["Error", "TimeoutError", "AssertionError"].includes(error.name) ? error.name : "Other", failureScreenshotSaved }, null, 2));
    throw error;
  }
  finally {
    const cleanupErrors = [];
    for (const context of contexts) { try { await context.close(); } catch (error) { cleanupErrors.push(error); } }
    try { await browser.close(); } catch (error) { cleanupErrors.push(error); }
    if (cleanupErrors.length > 0) { throw new AggregateError(original === undefined ? cleanupErrors : [original, ...cleanupErrors], "Desktop fixture cleanup did not fully settle."); }
  }
}

async function deadline(promise, message) {
  let timer;
  try {
    return await Promise.race([promise, new Promise((_, reject) => { timer = setTimeout(() => reject(new Error(message)), 10000); })]);
  } finally { clearTimeout(timer); }
}
