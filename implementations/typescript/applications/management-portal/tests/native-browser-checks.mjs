// Actual desktop UI on the ordinary production host. Controlled responses below test only browser race/paging states.
import assert from "node:assert/strict";
import { mkdir, writeFile } from "node:fs/promises";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { randomUUID } from "node:crypto";
import { chromium, expect } from "@playwright/test";
import { capturePortalContext, portalContextHeader, portalContextKey } from "../src/portal-context.ts";

export async function verifyBrowser({ origin, applicationA, applicationB, registrationA, operatorAContext, operatorBContext, lookupOperatorContext, rollout, adoptionObservations, r08 = false, reviewCorrections = false, versionWorkflow = false, instanceStatus = false, firstVersion = false,
  newSaveRecovery = false, releaseConflict = false, configurationDeploy, configurationBoundary, instanceStatusBoundaries,
  configurationMenus, sessionTabsContexts, editorRefresh, refreshOperatorContext }) {
  const root = resolve(dirname(fileURLToPath(import.meta.url)), "../../../../..");
  const evidence = join(root, ".artifacts/management-portal/CIS-0029/WORK-005", "browser-" + randomUUID());
  await mkdir(evidence, { recursive: true });
  // The version scenario also proves native window resizing; its existing
  // browser owner is headed rather than adding another browser or harness.
  const browser = await chromium.launch({ channel: "msedge", headless: !versionWorkflow && sessionTabsContexts === undefined });
  const contexts = [];
  const cases = [];
  const errors = [];
  const metadataAppliedKeys = [];
  const metadataConflictKeys = [];
  let original;
  let stage = "launch";
  let activePage;
  async function context(api, nativeViewport = false) {
    const result = await browser.newContext({ storageState: await api.storageState(), viewport: nativeViewport ? null : { width: 1600, height: 1000 },
      // Browser-only trust exception for this newly generated test root. All server transports validate the explicit root.
      ignoreHTTPSErrors: true });
    result.on("page", page => page.on("pageerror", error => errors.push(error.name + ": " + error.message.slice(0, 256))));
    contexts.push(result);
    return result;
  }
  async function screenshot(page, name) { await page.screenshot({ path: join(evidence, name + ".png"), fullPage: true }); }
  try {
    if (configurationMenus != null || sessionTabsContexts !== undefined || editorRefresh != null) {
      stage = configurationMenus != null ? "configuration-menus" : editorRefresh != null ? "configuration-editor-refresh" : "session-tabs";
      const a = await context(sessionTabsContexts?.initial ?? operatorAContext);
      const page = await a.newPage();
      activePage = page;
      const proofName = configurationMenus != null ? "configurationMenus" : editorRefresh != null ? "editorRefresh" : "sessionTabs";
      let proof;
      if (configurationMenus != null) {
        const { verifyConfigurationMenusBrowser } = await import("./native-configuration-menu-checks.mjs");
        proof = await verifyConfigurationMenusBrowser({ page, origin, applicationA, configurationMenus, screenshot, cases });
      } else if (editorRefresh != null) {
        const { verifyConfigurationEditorRefreshBrowser } = await import("./native-configuration-refresh-checks.mjs");
        proof = await verifyConfigurationEditorRefreshBrowser({ page, origin, applicationA, refreshOperatorContext, editorRefresh, screenshot, cases });
      } else {
        const secondPage = await a.newPage();
        const { verifySessionTabsBrowser } = await import("./native-session-tabs-checks.mjs");
        proof = await verifySessionTabsBrowser({ page, secondPage, origin, applicationA, operatorBContext,
          renewedOperatorAContext: sessionTabsContexts.renewed, staleOperatorAContext: sessionTabsContexts.stale,
          newerOperatorAContext: sessionTabsContexts.newer, screenshot, cases });
      }
      assert.deepEqual(errors, []);
      await writeFile(join(evidence, "browser-evidence.json"), JSON.stringify({ passed: true, cases, [proofName]: true }, null, 2));
      return { cases, metadataAppliedKeys, metadataConflictKeys, [proofName]: proof };
    }
    if (configurationBoundary != null || instanceStatusBoundaries != null) {
      stage = configurationBoundary != null ? "configuration-boundary" : "instance-status-boundaries";
      const a = await context(operatorAContext);
      const page = await a.newPage();
      activePage = page;
      const b = await context(operatorBContext);
      const pageB = await b.newPage();
      const proofName = configurationBoundary != null ? "configurationBoundary" : "instanceStatusBoundaries";
      let proof;
      if (configurationBoundary != null) {
        const { verifyConfigurationBoundaryBrowser } = await import("./native-configuration-boundaries-checks.mjs");
        proof = await verifyConfigurationBoundaryBrowser({ page, pageB, origin, applicationA, applicationB,
          operatorAContext, operatorBContext, configurationBoundary, screenshot, cases });
      } else {
        const { verifyInstanceStatusBoundariesBrowser } = await import("./native-instance-status-boundaries-checks.mjs");
        proof = await verifyInstanceStatusBoundariesBrowser({ page, origin, applicationA, applicationB,
          instanceStatusBoundaries, screenshot, cases });
      }
      assert.deepEqual(errors, []);
      await writeFile(join(evidence, "browser-evidence.json"), JSON.stringify({ passed: true, cases, [proofName]: true }, null, 2));
      return { cases, metadataAppliedKeys, metadataConflictKeys, [proofName]: proof };
    }
    if (newSaveRecovery || releaseConflict || configurationDeploy !== null && configurationDeploy !== undefined) {
      stage = newSaveRecovery ? "configuration-save-recovery" : releaseConflict ? "configuration-release-conflict" : "configuration-deployment";
      const a = await context(operatorAContext);
      const page = await a.newPage();
      activePage = page;
      let proof;
      const proofName = newSaveRecovery ? "configurationSave" : releaseConflict ? "releaseConflict" : "configurationDeploy";
      if (configurationDeploy !== null && configurationDeploy !== undefined) {
        const b = await context(operatorBContext);
        const pageB = await b.newPage();
        const { verifyConfigurationDeployBrowser } = await import("./native-config-deploy-consumer.mjs");
        proof = await verifyConfigurationDeployBrowser({ page, pageB, origin, applicationA, applicationB, configurationDeploy, screenshot, cases });
      } else {
        const { verifyNewConfigurationSaveRecoveryBrowser, verifyConfigurationReleaseConflictBrowser } = await import("./native-config-save-consumer.mjs");
        proof = await (newSaveRecovery ? verifyNewConfigurationSaveRecoveryBrowser : verifyConfigurationReleaseConflictBrowser)({ page, origin, applicationA, screenshot, cases });
      }
      assert.deepEqual(errors, []);
      await writeFile(join(evidence, "browser-evidence.json"), JSON.stringify({ passed: true, cases, [proofName]: true }, null, 2));
      return { cases, metadataAppliedKeys, metadataConflictKeys, [proofName]: proof };
    }
    if (firstVersion) {
      stage = "configuration-first-version";
      const a = await context(operatorAContext);
      const page = await a.newPage();
      activePage = page;
      const { verifyFirstConfigurationVersionBrowser } = await import("./native-configuration-versions-checks.mjs");
      const proof = await verifyFirstConfigurationVersionBrowser({ page, origin, applicationA, screenshot, cases });
      assert.deepEqual(errors, []);
      await writeFile(join(evidence, "browser-evidence.json"), JSON.stringify({ passed: true, cases, firstVersion: true }, null, 2));
      return { cases, metadataAppliedKeys, metadataConflictKeys, firstVersion: proof };
    }
    if (instanceStatus) {
      stage = "configuration-instance-status";
      const a = await context(operatorAContext);
      const page = await a.newPage();
      activePage = page;
      const lookup = await context(lookupOperatorContext);
      const lookupPage = await lookup.newPage();
      const { verifyInstanceStatusBrowser } = await import("./native-instance-status-checks.mjs");
      const proof = await verifyInstanceStatusBrowser({ page, lookupPage, origin, applicationA, adoptionObservations, screenshot, cases });
      assert.deepEqual(errors, []);
      await writeFile(join(evidence, "browser-evidence.json"), JSON.stringify({ passed: true, cases, instanceStatus: true }, null, 2));
      return { cases, metadataAppliedKeys, metadataConflictKeys, instanceStatus: proof };
    }
    if (versionWorkflow) {
      stage = "configuration-version-workflow";
      const a = await context(operatorAContext, true);
      const page = await a.newPage();
      activePage = page;
      const b = await context(operatorBContext);
      const pageB = await b.newPage();
      const { verifyConfigurationVersionsBrowser } = await import("./native-configuration-versions-checks.mjs");
      const proof = await verifyConfigurationVersionsBrowser({ page, pageB, origin, applicationA, applicationB, screenshot, cases });
      assert.deepEqual(errors, []);
      await writeFile(join(evidence, "browser-evidence.json"), JSON.stringify({ passed: true, cases, versionWorkflow: true, windowGeometry: proof.windowGeometry }, null, 2));
      return { cases, metadataAppliedKeys, metadataConflictKeys, versionWorkflow: proof };
    }
    stage = "applications-filtering";
    const a = await context(operatorAContext);
    const page = await a.newPage();
    activePage = page;
    await page.goto(origin + "/applications");
    await expect(page.getByRole("heading", { name: "Applications", exact: true })).toBeVisible();
    await expect(page.getByRole("link", { name: "managed-a", exact: true })).toBeVisible();
    await expect(page.getByRole("link", { name: "managed-b", exact: true })).toHaveCount(0);
    await expect(page.getByLabel("Environment", { exact: true }).getByRole("option", { name: "Development", exact: true })).toHaveCount(1);
    await expect(page.getByText(applicationA, { exact: true })).not.toBeVisible();
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
    const currentInstances = page.getByRole("region", { name: "Current instances", exact: true });
    const pastInstances = page.getByRole("region", { name: "Past instances", exact: true });
    await expect(currentInstances.getByRole("button", { name: "seed-a", exact: true })).toBeVisible();
    await expect(currentInstances.getByRole("button", { name: "seed-a-previous", exact: true })).toHaveCount(0);
    const pastInstance = page.getByRole("button", { name: "seed-a-previous", exact: true });
    await expect(pastInstance).toBeVisible();
    const pastRow = pastInstances.getByRole("row").filter({ has: pastInstance });
    await expect(pastRow).toContainText("0.9.0");
    await expect(pastRow).toContainText("Closed");
    cases.push("native-expanded-current-and-closed-past-instance-sections");
    try {
      for (const width of [900, 1280]) {
        await page.setViewportSize({ width, height: 900 });
        await expect(instance).toBeVisible(); await expect(pastInstance).toBeVisible();
        const sizing = await page.evaluate(() => {
          const main = document.querySelector("main").getBoundingClientRect();
          return { viewport: document.documentElement.clientWidth, page: document.documentElement.scrollWidth, main: { left: main.left, right: main.right, width: main.width },
            frames: [...document.querySelectorAll(".instance-section .table-frame")].map(frame => { const box = frame.getBoundingClientRect(); return { left: box.left, right: box.right, width: box.width }; }) };
        });
        assert.equal(sizing.frames.length, 2);
        assert.ok(sizing.page <= sizing.viewport + 1, "Instance tables must not widen the desktop document.");
        assert.ok(sizing.frames.every(frame => frame.left >= sizing.main.left - 1 && frame.right <= sizing.main.right + 1 && frame.width <= sizing.main.width + 1),
          "Each Current/Past table keeps its own scroll frame within the application page.");
        await expect(pastRow).toContainText("0.9.0"); await expect(pastRow).toContainText("Closed");
        if (width === 900) { await screenshot(page, "instances-narrow-desktop-native"); }
      }
    } finally { await page.setViewportSize({ width: 1600, height: 1000 }); }
    cases.push("native-instance-tables-stay-within-resized-desktop");
    await screenshot(page, "instances-native");
    await instance.focus();
    await page.keyboard.press("Enter");
    const panel = page.getByRole("dialog");
    await expect(panel.getByRole("heading", { name: "seed-a", exact: true })).toBeVisible();
    await expect(panel).toContainText("Reported app version");
    await expect(panel.getByText(registrationA, { exact: true })).not.toBeVisible();
    await screenshot(page, "instance-panel-native");
    const technical = panel.locator("details.technical-details");
    await technical.locator("summary").focus();
    await page.keyboard.press("Enter");
    await expect(panel.getByText(registrationA, { exact: true })).toBeVisible();
    // Controlled clipboard boundary: exercise the actual button/status without reading or replacing the user's system clipboard.
    await page.evaluate(() => {
      Object.defineProperty(navigator, "clipboard", { configurable: true, value: { writeText: async value => { globalThis.__cisCopiedIdentifier = value; } } });
    });
    try {
      await panel.getByRole("button", { name: "Copy Registration ID", exact: true }).focus();
      await page.keyboard.press("Enter");
      await expect(technical.getByRole("status")).toHaveText("Registration ID copied.");
      assert.equal(await page.evaluate(() => globalThis.__cisCopiedIdentifier), registrationA);
    } finally {
      await page.evaluate(() => { delete navigator.clipboard; delete globalThis.__cisCopiedIdentifier; });
    }
    cases.push("native-keyboard-technical-disclosure", "controlled-copy-action-preserves-exact-identifier");
    await technical.locator("summary").click();
    await page.keyboard.press("Escape");
    await expect(panel).toHaveCount(0);
    await expect(instance).toBeFocused();
    cases.push("native-filter-environment-empty", "native-per-registration-versions", "keyboard-panel-escape-focus-return");

    stage = "configuration-and-capabilities";
    await page.getByRole("tab", { name: "Configuration", exact: true }).click();
    await expect(page.getByRole("heading", { name: "Configurations", exact: true })).toBeVisible();
    const configuration = page.getByRole("button", { name: "shared.settings", exact: true });
    await expect(configuration).toBeVisible();
    await screenshot(page, "configuration-native");
    await configuration.click();
    await expect(page).toHaveURL(url => url.pathname.endsWith("/configuration/shared-settings") && url.searchParams.get("applicationId") === applicationA
      && url.searchParams.get("configurationId") === "shared.settings" && url.searchParams.get("schemaVersion") === "1");
    await expect(page.getByRole("region", { name: "Saved versions", exact: true })).toBeVisible();
    const selectedConfiguration = await protectedReply(page, origin + `/api/portal/applications/${applicationA}/configurations/shared.settings/details?schemaVersion=1`);
    assert.equal(selectedConfiguration.value.content.state, rollout === null || rollout === undefined ? "unassigned" : "redacted");
    if (rollout === null || rollout === undefined) { await expect(page.locator(".configuration-deployment-bar")).toContainText("Not deployed"); }
    const saved = await protectedReply(page, origin + `/api/portal/applications/${applicationA}/configurations/shared.settings/releases?schemaVersion=1&pageSize=10`);
    assert.ok(saved.value.items.length > 0, "The seeded immutable history is inspectable without claiming it is deployed.");
    const inspected = await protectedReply(page, origin + `/api/portal/applications/${applicationA}/configurations/shared.settings/versions/${saved.value.items[0].releaseId}?schemaVersion=1`);
    if (inspected.value.content.state === "present") { await expect(page.locator(".configuration-json-readonly")).toBeVisible(); }
    else { await expect(page.getByRole("region", { name: "Selected version", exact: true })).toContainText(inspected.value.content.message); }
    await expect(page.getByText("PORTAL_PRIVATE_DEFAULT", { exact: false })).toHaveCount(0);
    const definitions = await page.request.get(origin + `/api/portal/applications/${applicationA}/configuration`);
    const definitionText = await definitions.text();
    assert.equal(definitions.status(), 200);
    assert.ok(!definitionText.includes("PORTAL_PRIVATE_DEFAULT") && !definitionText.includes("defaultsJson"));
    await screenshot(page, "configuration-version-native");
    await page.getByRole("navigation", { name: "Configuration breadcrumb", exact: true }).getByRole("button", { name: "Configuration", exact: true }).focus();
    await page.keyboard.press("Enter");
    await expect(page.getByRole("tab", { name: "Configuration", exact: true })).toHaveAttribute("aria-selected", "true");
    assert.equal(new URL(page.url()).searchParams.get("fromQuery"), "managed-a");
    assert.equal(new URL(page.url()).searchParams.get("fromEnvironment"), "Development");
    await page.getByRole("tab", { name: "Capabilities", exact: true }).click();
    for (const name of ["Schedulable job types", "Registered RuleEngine instances", "Trigger providers"]) {
      await expect(page.getByRole("heading", { name, exact: true })).toBeVisible();
    }
    await expect(page.locator(".feature-groups .badge")).toHaveText(["Unavailable", "Unavailable", "Unavailable"]);
    await expect(page.locator(".feature-groups .item-link")).toHaveCount(0);
    await expect(page.getByRole("button", { name: /Create Scheduled Task|Create named trigger|Run now/u })).toHaveCount(0);
    await expect(page.locator(".service-api-details table")).not.toBeVisible();
    await screenshot(page, "capabilities-native");
    await page.locator(".service-api-details > summary").click();
    await expect(page.locator(".service-api-details tbody tr").first()).toBeVisible();
    await page.locator(".service-api-details tbody tr .item-link").first().click();
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
    await expect(page).toHaveURL(url => url.pathname.endsWith("/jobs/details") && url.searchParams.get("applicationId") === applicationA);
    await expect(page.getByRole("tab", { name: "Overview", exact: true })).toHaveAttribute("aria-selected", "true");
    const full = new URL(page.url());
    assert.ok(full.pathname.endsWith("/jobs/details")); assert.equal(full.searchParams.get("applicationId"), applicationA);
    for (const [key, expected] of Object.entries({ registration: registrationA, report: "1", scope: "customer-service", job: "customer.dormancy",
      resource: "partition-eu", mode: "scheduled", fromQuery: "managed-a", fromEnvironment: "Development" })) {
      assert.equal(full.searchParams.get(key), expected, `Full details preserves ${key}.`);
    }
    await expect(page.getByRole("tab", { name: "Overview", exact: true })).toHaveAttribute("aria-selected", "true");
    await expect(page.getByRole("heading", { name: "Job definition", exact: true })).toBeVisible();
    await expect(page.locator(".report-facts").getByText(registrationA, { exact: true })).not.toBeVisible();
    await page.locator(".report-facts summary").click();
    await expect(page.locator(".report-facts").getByText(registrationA, { exact: true })).toBeVisible();
    await page.locator(".report-facts summary").click();
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
    cases.push("native-metadata-only-configuration", "native-feature-catalog-unavailable-and-collapsed-api-diagnostics", "native-whole-inventory-and-exact-report-navigation", "native-reported-local-configuration", "truthful-history-task-event-unavailable");

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
    const controlledApplication = await protectedReply(page, origin + `/api/portal/applications/${applicationA}`);
    const controlledContext = controlledApplication.context;
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
        await route.fulfill({ json: { state: "ready", context: controlledContext, value: { items: [{ kind: "application", configurationId: "late-stale-record", schemaVersion: "1", sourceId: "source", applicationId: applicationA, environment: "Development", bindingId: "binding", hostApplicationName: "host" }] } } });
      } catch (error) {
        if (route.request().failure()?.errorText !== "net::ERR_ABORTED") { routeFailure = error; }
      } finally { settle(); }
    });
    try {
      await page.getByRole("tab", { name: "Configuration", exact: true }).click();
      await deadline(started, "The controlled old-tab request must enter.");
      await page.getByRole("tab", { name: "Capabilities", exact: true }).click();
      await expect(page.getByRole("heading", { name: "Schedulable job types", exact: true })).toBeVisible();
      await page.locator(".service-api-details > summary").click();
      await expect(page.locator(".service-api-details tbody tr").first()).toBeVisible();
    } finally {
      release();
      if (routeEntered) { await deadline(settled, "The controlled old-tab response must settle."); }
      await page.unroute(configurationRoute);
    }
    if (routeFailure !== undefined) { throw routeFailure; }
    await page.evaluate(() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))));
    await expect(page.getByRole("tab", { name: "Capabilities", exact: true })).toHaveAttribute("aria-selected", "true");
    await expect(page.locator(".service-api-details tbody tr").first()).toBeVisible();
    await expect(page.getByText("late-stale-record")).toHaveCount(0);
    cases.push("controlled-delayed-old-tab-response-discarded");

    stage = "controlled-cross-hook-session-retirement";
    const currentApplication = await protectedReply(page, origin + `/api/portal/applications/${applicationA}`);
    await page.getByRole("tab", { name: "Instances", exact: true }).click();
    await expect(instance).toBeVisible();
    await expect(page.getByRole("button", { name: "Refresh", exact: true })).toBeEnabled();
    let releaseParent;
    let parentEntered;
    let parentSettled;
    let parentRouteEntered = false;
    let parentRouteFailure;
    let childRouteEntered = false;
    let childRouteFailure;
    let childSettled;
    const parentHeld = new Promise(resolve => { releaseParent = resolve; });
    const parentStarted = new Promise(resolve => { parentEntered = resolve; });
    const parentFinished = new Promise(resolve => { parentSettled = resolve; });
    const childFinished = new Promise(resolve => { childSettled = resolve; });
    const applicationRoute = `**/api/portal/applications/${applicationA}`;
    const instancesRoute = `**/api/portal/applications/${applicationA}/instances*`;
    await page.route(applicationRoute, async route => {
      parentRouteEntered = true;
      parentEntered();
      try {
        await parentHeld;
        await route.fulfill({ json: { state: "ready", context: currentApplication.context,
          value: { ...currentApplication.value, metadata: { ...currentApplication.value.metadata, displayName: "late-session-record" } } } });
      } catch (error) {
        if (route.request().failure()?.errorText !== "net::ERR_ABORTED") { parentRouteFailure = error; }
      } finally { parentSettled(); }
    });
    await page.route(instancesRoute, async route => {
      childRouteEntered = true;
      try {
        await deadline(parentStarted, "The parent refresh must be in flight before controlled session expiry.");
        await route.fulfill({ status: 401, json: { state: "unauthenticated", message: "The session expired." } });
      } catch (error) {
        if (route.request().failure()?.errorText !== "net::ERR_ABORTED") { childRouteFailure = error; }
      } finally { childSettled(); }
    });
    try {
      // The same Refresh starts the retained parent and selected child readers. Their shared session owner must retire both.
      await page.getByRole("button", { name: "Refresh", exact: true }).click();
      await deadline(parentStarted, "The controlled parent refresh must enter.");
      await expect(page.getByRole("main").getByRole("alert")).toContainText("Sign in required");
    } finally {
      releaseParent();
      if (parentRouteEntered) { await deadline(parentFinished, "The retired parent response must settle."); }
      if (childRouteEntered) { await deadline(childFinished, "The controlled child expiry must settle."); }
      await page.unroute(applicationRoute);
      await page.unroute(instancesRoute);
    }
    if (parentRouteFailure !== undefined) { throw parentRouteFailure; }
    if (childRouteFailure !== undefined) { throw childRouteFailure; }
    await page.evaluate(() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))));
    await expect(page.getByRole("main").getByRole("alert")).toContainText("Sign in required");
    await expect(page.getByRole("main").getByRole("alert").getByRole("link", { name: "Sign in", exact: true })).toBeVisible();
    await expect(page.getByRole("tablist")).toHaveCount(0);
    await expect(page.getByText("seed-a", { exact: true })).toHaveCount(0);
    await expect(page.getByText("late-session-record", { exact: true })).toHaveCount(0);
    cases.push("controlled-child-session-expiry-clears-content-and-explains-reauthentication", "controlled-cross-hook-late-ready-cannot-revive-retired-session");
    await screenshot(page, "child-session-expired");
    await page.unrouteAll({ behavior: "wait" });

    stage = "controlled-paging-filter-restart";
    // The preceding 401 was controlled; reacquire the still-valid native context before this full-page test restart.
    const pagingContext = (await protectedReply(page, origin + "/api/portal/applications")).context;
    const cursor = "controlled-next-page";
    const requestedPages = [];
    await page.route("**/api/portal/applications?*", route => {
      const url = new URL(route.request().url());
      requestedPages.push({ query: url.searchParams.get("query"), cursor: url.searchParams.get("cursor") });
      return route.fulfill({ json: { state: "ready", context: pagingContext, value: { items: [{ applicationId: url.searchParams.has("cursor") ? applicationB : applicationA,
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
    stage = "metadata-editor-review-and-original-recovery";
    activePage = page;
    const metadata = await verifyMetadataEditor({ page, pageB, origin, applicationA, applicationB, screenshot, cases });
    metadataAppliedKeys.push(...metadata.appliedKeys); metadataConflictKeys.push(...metadata.conflictKeys);
    let configurationProof, rolloutProof, r08Proof, configurationRepairs;
    if (reviewCorrections) {
      stage = "configuration-review-corrections";
      const { verifyConfigurationRepairsBrowser } = await import("./native-configuration-repairs-checks.mjs");
      configurationRepairs = await verifyConfigurationRepairsBrowser({ page, pageB, origin, applicationA, applicationB, screenshot, cases });
    } else if (r08) {
      stage = "r08-readable-creation-publication-assignment";
      const { verifyR08Browser } = await import("./native-r08-checks.mjs");
      r08Proof = await verifyR08Browser({ page, pageB, origin, applicationA, applicationB, screenshot, cases });
    } else if (rollout === null || rollout === undefined) {
      stage = "configuration-authoring-and-original-recovery";
      const { verifyConfigurationBrowser } = await import("./native-configuration-checks.mjs");
      configurationProof = await verifyConfigurationBrowser({ page, pageB, origin, applicationA, applicationB, screenshot, cases });
    } else {
      stage = "assignment-map-and-original-recovery";
      const { verifyRolloutBrowser, verifyAdoptionBrowser } = await import("./native-rollout-checks.mjs");
      rolloutProof = await verifyRolloutBrowser({ page, pageB, origin, applicationA, applicationB, rollout, screenshot, cases });
      stage = "configuration-adoption-observations";
      const lookupBrowser = await context(lookupOperatorContext), lookupPage = await lookupBrowser.newPage();
      activePage = lookupPage;
      await verifyAdoptionBrowser({ page, lookupPage, origin, applicationA, adoptionObservations, screenshot, cases });
    }
    assert.deepEqual(errors, [], "The connected desktop must have no browser runtime errors.");
    await writeFile(join(evidence, "browser-evidence.json"), JSON.stringify({ viewport: "1600x1000", additionalDesktopViewports: ["900x900", "1280x900"], installedBrowser: "msedge", productionHost: true,
      actualNativeCases: cases.filter(value => !value.startsWith("controlled-")), controlledBrowserCases: cases.filter(value => value.startsWith("controlled-")), screenshotsContainOnlyOwnedSyntheticFixtureData: true }, null, 2));
    return { metadataAppliedKeys, metadataConflictKeys, configuration: configurationProof, rollout: rolloutProof, r08: r08Proof, configurationRepairs };
  } catch (error) {
    original = error;
    let failureScreenshotSaved = false;
    if (activePage !== undefined) {
      try { await screenshot(activePage, "failure-" + stage); failureScreenshotSaved = true; }
      catch { /* The original assertion and all native cleanup remain required. */ }
    }
    const sourceLine = error instanceof Error ? /(native-browser-checks|native-config-save-consumer|native-config-deploy-consumer|native-configuration-menu-checks|native-deploy-session-checks|native-session-tabs-checks|native-configuration-refresh-checks|native-instance-status-boundaries-checks|native-configuration-boundaries-checks|native-instance-status-checks|native-configuration-versions-checks|native-configuration-checks|native-r08-checks|native-rollout-checks)\.mjs:(\d+):(\d+)/u.exec(error.stack ?? "") : null;
    // Source coordinates and fixed stages identify the failed assertion without persisting DOM, URLs or credentials.
    await writeFile(join(evidence, "browser-failure.json"), JSON.stringify({ stage, completedCases: cases,
      sourceFile: sourceLine === null ? null : sourceLine[1] + ".mjs",
      sourceLine: sourceLine === null ? null : Number(sourceLine[2]), sourceColumn: sourceLine === null ? null : Number(sourceLine[3]),
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

async function verifyMetadataEditor({ page, pageB, origin, applicationA, applicationB, screenshot, cases }) {
  await page.goto(origin + `/applications/${applicationA}`);
  const entry = page.getByRole("link", { name: "Display metadata", exact: true });
  await expect(entry).toBeVisible(); await entry.focus(); await page.keyboard.press("Enter");
  await expect(page.getByRole("heading", { name: "Edit display metadata", exact: true })).toBeVisible();
  const name = page.getByLabel("Display name", { exact: true });
  const description = page.getByLabel("Description", { exact: true });
  await expect(name).toBeEnabled(); await expect(description).toHaveValue("Description to clear");
  await name.fill("Discard candidate");
  let cancelDialog = false;
  page.once("dialog", async dialog => { assert.equal(dialog.type(), "confirm"); cancelDialog = true; await dialog.dismiss(); });
  await page.getByRole("button", { name: "Cancel", exact: true }).click();
  assert.equal(cancelDialog, true); await expect(name).toHaveValue("Discard candidate");
  let backObserved;
  let backDismissed = false;
  const backDialog = new Promise(resolve => { backObserved = resolve; });
  const dismissBack = async dialog => { assert.equal(dialog.type(), "beforeunload"); await dialog.dismiss(); backDismissed = true; backObserved(); };
  page.once("dialog", dismissBack);
  // Headless renderer keyboard events do not reliably invoke the browser-chrome Back accelerator.
  // This is the actual browser history navigation; the product's native beforeunload guard must refuse it.
  const backNavigation = page.goBack({ waitUntil: "commit", timeout: 3000 }).then(() => undefined, error => error);
  try {
    await deadline(backDialog, "The full-document editor must guard a real browser Back action.");
    const navigationFailure = await backNavigation;
    if (navigationFailure !== undefined) {
      assert.equal(backDismissed, true);
      const abortedNavigation = /ERR_ABORTED|NS_BINDING_ABORTED/u.test(navigationFailure.message);
      // Chromium can leave the navigation waiter pending after a native beforeunload dismissal.
      // Accept only this bounded commit wait after the exact observed cancellation, never an unobserved dialog timeout.
      const canceledCommitWait = navigationFailure.name === "TimeoutError"
        && navigationFailure.message.startsWith("page.goBack: Timeout 3000ms exceeded.");
      assert.equal(abortedNavigation || canceledCommitWait, true, "Only the observed canceled Back navigation may end without a commit.");
    }
  } finally { page.off("dialog", dismissBack); await backNavigation; }
  await expect(page).toHaveURL(origin + `/applications/${applicationA}/metadata`);
  await expect(name).toHaveValue("Discard candidate");
  cases.push("native-metadata-full-document-entry-and-dirty-cancel-back");
  await name.fill("Managed A reviewed");
  await page.getByRole("button", { name: "Clear description", exact: true }).click();
  await expect(description).toHaveValue("");
  await page.getByRole("button", { name: "Review changes", exact: true }).focus(); await page.keyboard.press("Enter");
  const review = page.getByRole("heading", { name: "Review display metadata", exact: true });
  await expect(review).toBeFocused();
  const descriptionRow = page.getByRole("row").filter({ has: page.getByRole("rowheader", { name: "Description", exact: true }) });
  await expect(descriptionRow).toContainText("Description to clear");
  await expect(descriptionRow).toContainText("Clear description — no description will remain");
  await screenshot(page, "metadata-review-native");

  // A real independent write makes the reviewed ETag stale. The UI must retain the draft for a new explicit review.
  const current = await protectedReply(page, origin + `/api/portal/applications/${applicationA}/metadata`);
  const concurrentKey = randomUUID();
  const concurrent = await page.request.put(origin + `/api/portal/applications/${applicationA}/metadata`, { headers: {
    Origin: origin, "Content-Type": "application/json", "Idempotency-Key": concurrentKey, "If-Match": current.value.metadataETag,
    [portalContextHeader]: portalContextKey(current.context) }, data: { commandVersion: 1, displayName: "managed-a", description: "Description to clear" } });
  assert.equal((await concurrent.json()).value.disposition, "applied");
  const conflictResponse = page.waitForResponse(response => response.request().method() === "PUT" && new URL(response.url()).pathname === `/api/portal/applications/${applicationA}/metadata`);
  await page.getByRole("button", { name: "Confirm metadata change", exact: true }).click();
  const conflict = await (await conflictResponse).json();
  assert.equal(conflict.value.disposition, "conflict");
  await expect(page.getByRole("heading", { name: "Metadata conflict — refresh and review again", exact: true })).toBeVisible();
  await expect(page.getByRole("button", { name: "Recover original result", exact: true })).toBeDisabled();
  await screenshot(page, "metadata-conflict-native");
  await page.getByRole("button", { name: "Dismiss resolved reference and refresh", exact: true }).click();
  await expect(name).toBeEnabled(); await expect(name).toHaveValue("Managed A reviewed"); await expect(description).toHaveValue("");
  await page.getByRole("button", { name: "Review changes", exact: true }).click();
  await expect(review).toBeFocused();
  cases.push("native-metadata-etag-conflict-retains-draft-for-deliberate-review");

  // Only delivery is controlled: route.fetch executes the real native write and its decoded result is checked first.
  const pattern = `**/api/portal/applications/${applicationA}/metadata`;
  let originalKey;
  let writes = 0;
  let routeFailure;
  let routeEntered = false;
  let released;
  const joined = new Promise(resolve => { released = resolve; });
  await page.route(pattern, async route => {
    if (route.request().method() !== "PUT") { await route.continue(); return; }
    routeEntered = true;
    let actual;
    try {
      writes++;
      originalKey = route.request().headers()["idempotency-key"];
      assert.match(originalKey, /^[0-9a-f-]{36}$/u);
      const retained = await page.evaluate(() => sessionStorage.getItem("cis.management.command-receipts.v1"));
      assert.ok(retained.includes(originalKey), "The exact original key is already retained before the actual PUT.");
      assert.equal(retained.includes("Managed A reviewed"), false, "Receipt storage contains no draft.");
      actual = await route.fetch({ timeout: 30000 });
      const body = await actual.json();
      assert.equal(body.state, "ready"); assert.equal(body.value.disposition, "applied");
      assert.equal(body.value.key, originalKey); assert.equal(body.value.decision.metadata.displayName, "Managed A reviewed");
      assert.equal(body.value.decision.metadata.description, undefined);
      await route.abort("failed");
    } catch (error) { routeFailure = error; try { await route.abort("failed"); } catch { /* Preserve the real failure after native settlement. */ } }
    finally { if (actual !== undefined) { await actual.dispose(); } released(); }
  });
  try {
    await page.getByRole("button", { name: "Confirm metadata change", exact: true }).focus(); await page.keyboard.press("Enter");
    await deadline(joined, "The actual native metadata write and controlled response delivery must settle.");
    if (routeFailure !== undefined) { throw routeFailure; }
    await expect(page.getByRole("button", { name: "Recover original result", exact: true })).toBeVisible();
    await expect(page.getByRole("status").filter({ hasText: "Waiting for the current request" })).toHaveCount(0);
  } finally { if (routeEntered) { await joined; } await page.unroute(pattern); }
  const acceptReload = async dialog => { assert.equal(dialog.type(), "beforeunload"); await dialog.accept(); };
  page.once("dialog", acceptReload);
  try { await page.reload(); } finally { page.off("dialog", acceptReload); }
  await expect(page.getByRole("button", { name: "Recover original result", exact: true })).toBeEnabled();
  await expect(name).toHaveValue("Managed A reviewed"); await expect(description).toHaveValue("");
  await expect(page.getByRole("button", { name: "Review changes", exact: true })).toBeDisabled();
  await page.getByRole("button", { name: "Recover original result", exact: true }).focus(); await page.keyboard.press("Enter");
  await expect(page.getByRole("heading", { name: "Metadata change committed", exact: true })).toBeVisible();
  await expect(page.getByRole("button", { name: "Recover original result", exact: true })).toBeDisabled();
  assert.equal(writes, 1, "Reload/recovery must not retransmit the original write or replace its key.");
  await screenshot(page, "metadata-original-result-native");
  await page.getByRole("button", { name: "Dismiss resolved reference and refresh", exact: true }).click();
  await expect(name).toBeEnabled(); await expect(name).toHaveValue("Managed A reviewed");
  await expect(page.getByRole("button", { name: "Review changes", exact: true })).toBeDisabled();
  cases.push("native-metadata-keyboard-review-and-explicit-description-clear", "controlled-lost-delivery-after-native-commit-and-same-key-reload-recovery");

  const returnedDescription = "Application description visible after confirmed metadata update";
  await description.fill(returnedDescription);
  await page.getByRole("button", { name: "Review changes", exact: true }).click();
  await expect(review).toBeFocused();
  const metadataPath = `/api/portal/applications/${applicationA}/metadata`;
  let confirmedWrites = 0, unexpectedDepartureDialogs = 0;
  const countWrite = request => { if (request.method() === "PUT" && new URL(request.url()).pathname === metadataPath) { confirmedWrites++; } };
  const dismissDeparture = async dialog => { unexpectedDepartureDialogs++; await dialog.dismiss(); };
  page.on("request", countWrite); page.on("dialog", dismissDeparture);
  let confirmation, confirmationFailure, confirmationSettled;
  let confirmationEntered = false;
  const confirmationFinished = new Promise(resolve => { confirmationSettled = resolve; });
  const confirmationRoute = async route => {
    if (route.request().method() !== "PUT") { await route.continue(); return; }
    confirmationEntered = true;
    let response;
    try {
      // The application navigates after consuming this response. Capture the real native body before that document disappears.
      response = await route.fetch({ timeout: 30000 });
      assert.equal(response.status(), 200);
      const confirmed = await response.json();
      assert.equal(confirmed.state, "ready"); assert.equal(confirmed.value.disposition, "applied"); assert.equal(confirmed.value.originalCompletion, "committed");
      assert.equal(confirmed.value.decision.metadata.description, returnedDescription);
      assert.equal(confirmed.value.key, route.request().headers()["idempotency-key"]);
      confirmation = confirmed.value;
      await route.fulfill({ response });
    } catch (error) {
      confirmationFailure = error;
      try { await route.abort("failed"); } catch { /* Retain the original fault until owned response cleanup settles. */ }
    } finally {
      try { if (response !== undefined) { await response.dispose(); } }
      catch (error) { confirmationFailure = confirmationFailure === undefined ? error : new AggregateError([confirmationFailure, error], "Metadata response cleanup failed."); }
      finally { confirmationSettled(); }
    }
  };
  await page.route(origin + metadataPath, confirmationRoute);
  try {
    await page.getByRole("button", { name: "Confirm metadata change", exact: true }).click();
    await expect.poll(() => confirmationEntered).toBe(true);
    await confirmationFinished;
    if (confirmationFailure !== undefined) { throw confirmationFailure; }
    assert.ok(confirmation);
    await expect(page).toHaveURL(url => url.pathname.endsWith("/instances") && url.searchParams.get("applicationId") === applicationA);
    const returnedTitle = page.getByRole("heading", { name: "Managed A reviewed", exact: true });
    const returnedText = page.getByText(returnedDescription, { exact: true });
    await expect(returnedTitle).toBeVisible(); await expect(returnedText).toBeVisible();
    const titleBox = await returnedTitle.boundingBox(), descriptionBox = await returnedText.boundingBox();
    assert.ok(titleBox !== null && descriptionBox !== null && descriptionBox.y >= titleBox.y + titleBox.height - 1, "The authorized description appears below the application title.");
    const originalReferences = await page.evaluate(() => sessionStorage.getItem("cis.management.command-receipts.v1"));
    assert.ok(originalReferences?.includes(confirmation.key), "Automatic return retains the minimal original reference.");
    assert.equal(originalReferences.includes(returnedDescription), false, "The returned page stores no metadata draft in receipts.");
    await screenshot(page, "metadata-confirmed-application-return-native");
    await page.getByRole("link", { name: "Display metadata", exact: true }).click();
    await expect(page.getByRole("heading", { name: "Edit display metadata", exact: true })).toBeVisible();
    await expect(description).toHaveValue(returnedDescription);
    await page.getByRole("button", { name: "Back to application", exact: false }).click();
    await expect(page).toHaveURL(url => url.pathname.endsWith("/instances") && url.searchParams.get("applicationId") === applicationA);
    await expect(page.getByText(returnedDescription, { exact: true })).toBeVisible();
    assert.equal(confirmedWrites, 1, "Back navigation performs no additional metadata write.");
    assert.equal(unexpectedDepartureDialogs, 0, "A matching committed draft is cleared before return and does not claim unsaved changes.");
  } finally {
    try { if (confirmationEntered) { await confirmationFinished; } await page.unroute(origin + metadataPath, confirmationRoute); }
    finally { page.off("request", countWrite); page.off("dialog", dismissDeparture); }
  }
  cases.push("native-fresh-metadata-confirmation-returns-to-exact-application-with-description", "native-metadata-back-to-application-does-not-write-or-discard-a-new-draft");

  await pageB.goto(origin + `/applications/${applicationB}/metadata`);
  await expect(pageB.getByLabel("Display name", { exact: true })).toBeDisabled();
  await expect(pageB.getByRole("button", { name: "Review changes", exact: true })).toHaveCount(0);
  await expect(pageB.getByRole("button", { name: "Confirm metadata change", exact: true })).toHaveCount(0);
  cases.push("native-read-only-metadata-view-has-no-submit-action");
  return { appliedKeys: [concurrentKey, originalKey, confirmation.key], conflictKeys: [conflict.value.key] };
}

async function protectedReply(page, url) {
  const response = await page.request.get(url);
  assert.equal(response.status(), 200, "Controlled ready data must use context from a current actual protected reply.");
  const body = await response.json();
  assert.equal(body.state, "ready");
  const context = capturePortalContext(body.context);
  assert.ok(context && context.expiresAtMs > Date.now(), "A validated current session must own controlled ready replies.");
  return { value: body.value, context };
}

async function deadline(promise, message) {
  let timer;
  try {
    return await Promise.race([promise, new Promise((_, reject) => { timer = setTimeout(() => reject(new Error(message)), 10000); })]);
  } finally { clearTimeout(timer); }
}
