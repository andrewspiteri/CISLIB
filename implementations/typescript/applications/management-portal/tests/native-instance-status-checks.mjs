// Focused read-only continuation of the existing real Management/Auth.js/PostgreSQL fixture.
import assert from "node:assert/strict";
import { expect } from "@playwright/test";
import { configurationRoute } from "../src/portal-routes.ts";

export async function verifyInstanceStatusBrowser({ page, lookupPage, origin, applicationA, adoptionObservations, screenshot, cases }) {
  const base = `/api/portal/applications/${applicationA}/configurations/shared.settings`;
  const mapUrl = origin + base + "/assignments?schemaVersion=1";
  const observationsUrl = origin + base + "/adoption?schemaVersion=1&pageSize=20";
  // This native baseline is obtained after the unchanged metadata API prelude.
  const before = await get(page, mapUrl);
  const native = await get(page, observationsUrl);
  const releases = await get(page, origin + base + "/releases?schemaVersion=1&pageSize=100");
  const historical = native.items.find(row => row.registrationId === adoptionObservations[0]);
  const faulted = native.items.find(row => row.registrationId === adoptionObservations[1]);
  assert.ok(historical?.reported?.applied && historical.desired);
  assert.notEqual(historical.reported.applied.releaseId, historical.desired.releaseId);
  assert.equal(faulted?.reported?.state, "Faulted");
  assert.equal(faulted.reported.applied, undefined);
  assert.ok(faulted.reported.lastSuccessful);
  assert.ok(["pending", "unknown"].includes(historical.convergence));
  assert.ok(["faulted", "pending", "unknown"].includes(faulted.convergence));
  const appliedRelease = releases.items.find(release => release.releaseId === historical.reported.applied.releaseId);
  assert.ok(appliedRelease?.reason, "The real retained history supplies the old accepted version label.");
  const mutations = [];
  const observe = request => {
    const url = new URL(request.url());
    if (url.origin === origin && ["POST", "PUT", "PATCH", "DELETE"].includes(request.method())) {
      mutations.push(request.method() + " " + url.pathname);
    }
  };
  page.on("request", observe);
  lookupPage.on("request", observe);
  try {
    const route = configurationRoute({ applicationId: applicationA, applicationName: "managed-a", configurationId: "shared.settings", schemaVersion: "1", view: "adoption" });
    const observedPage = await navigate(page, origin + route, observationsUrl);
    const displayedHistorical = observedPage.items.find(row => row.registrationId === adoptionObservations[0]);
    const displayedFaulted = observedPage.items.find(row => row.registrationId === adoptionObservations[1]);
    assert.ok(displayedHistorical);
    assert.ok(displayedFaulted);
    await expect(page.getByRole("heading", { name: "Instance status", exact: true })).toBeVisible();
    const table = page.getByRole("table");
    await expect(table.getByRole("columnheader")).toHaveText(["Instance", "Configuration version in use", "Status"]);
    await expect(table.locator("tbody").getByRole("row")).toHaveCount(observedPage.items.filter(row => row.registrationId !== undefined).length);
    await expect(table.getByRole("button", { name: "No observed instances", exact: true })).toHaveCount(0);
    await expect(table.getByRole("button", { name: "Target observation", exact: true })).toHaveCount(0);
    const targetDiagnostics = page.locator(".instance-status-targets details");
    await expect(targetDiagnostics).toHaveCount(observedPage.items.some(row => row.registrationId === undefined) ? 1 : 0);
    if (observedPage.items.some(row => row.registrationId === undefined)) {
      await expect(targetDiagnostics).not.toHaveAttribute("open", "");
    }
    await expect(page.getByText("Last reported Configuration per instance", { exact: true })).toBeVisible();
    const historicalButton = page.getByRole("button", { name: "adoption-historical", exact: true });
    const historicalRow = table.getByRole("row").filter({ has: historicalButton });
    await expect(historicalRow.getByRole("cell").nth(2).locator(".badge")).toHaveText(seedStatus(displayedHistorical));
    await expect(historicalRow).not.toContainText("Up to date");
    await expect(historicalRow.getByRole("cell").nth(1)).toContainText(appliedRelease.reason);
    await expect(historicalRow.getByRole("cell").nth(1).locator("time")).toHaveAttribute("datetime", appliedRelease.createdAtUtc);
    await expect(historicalRow).not.toContainText(historical.reported.applied.releaseId);
    await expect(historicalRow).not.toContainText("Generation");
    const faultedRow = table.getByRole("row").filter({ has: page.getByRole("button", { name: "adoption-faulted", exact: true }) });
    await expect(faultedRow.getByRole("cell").nth(1)).toHaveText("Not reported");
    await expect(faultedRow.getByRole("cell").nth(2).locator(".badge")).toHaveText(seedStatus(displayedFaulted));
    cases.push("native-instance-status-three-columns-show-actual-old-accepted-version-without-false-current-success-or-failure");
    await screenshot(page, "instance-status-read-only-observations-native");

    // The lookup operator has AdoptionRead, but no AppRead/DefinitionsRead/ContentRead.
    await refused(lookupPage, origin + `/api/portal/applications/${applicationA}`);
    await refused(lookupPage, origin + base + "/releases?schemaVersion=1&pageSize=100");
    const low = await get(lookupPage, observationsUrl);
    assert.ok(low.items.some(row => row.registrationId === adoptionObservations[0]));
    await lookupPage.goto(origin + route);
    await expect(lookupPage.getByRole("heading", { name: "Instance status", exact: true })).toBeVisible();
    const lowTable = lookupPage.getByRole("table");
    await expect(lowTable.getByRole("columnheader")).toHaveText(["Instance", "Configuration version in use", "Status"]);
    const lowHistorical = lowTable.getByRole("row").filter({ has: lookupPage.getByRole("button", { name: "adoption-historical", exact: true }) });
    await expect(lowHistorical.getByRole("cell").nth(1)).toHaveText("Version details unavailable");
    await expect(lowHistorical).not.toContainText(appliedRelease.reason);
    await expect(lowHistorical).not.toContainText(historical.reported.applied.releaseId);
    cases.push("native-adoption-only-operator-keeps-instance-observations-with-explicit-unavailable-version-metadata");

    const faultedButton = lookupPage.getByRole("button", { name: "adoption-faulted", exact: true });
    await faultedButton.focus();
    await lookupPage.keyboard.press("Enter");
    const panel = lookupPage.getByRole("dialog");
    await expect(panel.getByRole("heading", { name: "adoption-faulted", exact: true })).toBeVisible();
    const technical = panel.locator("details.technical-details");
    await expect(technical).not.toHaveAttribute("open", "");
    await expect(panel.getByText(adoptionObservations[1], { exact: true })).not.toBeVisible();
    await technical.locator("summary").focus();
    await lookupPage.keyboard.press("Enter");
    await expect(technical.getByText(adoptionObservations[1], { exact: true })).toBeVisible();
    await expect(technical.locator(".technical-field").filter({ has: lookupPage.locator("dt").getByText("Reported state", { exact: true }) }).locator("dd")).toContainText("Faulted");
    await expect(technical.locator(".technical-field").filter({ has: lookupPage.locator("dt").getByText("Candidate failure code", { exact: true }) }).locator("dd")).toContainText("Configuration.LiveSetter");
    await expect(technical.locator(".technical-field").filter({ has: lookupPage.locator("dt").getByText("Historical successful release ID", { exact: true }) }).locator("dd")).toContainText(faulted.reported.lastSuccessful.releaseId);
    await lookupPage.evaluate(() => {
      globalThis.__cisInstanceStatusClipboard = Object.getOwnPropertyDescriptor(navigator, "clipboard");
      Object.defineProperty(navigator, "clipboard", { configurable: true, value: { writeText: async text => { globalThis.__cisInstanceStatusCopied = text; } } });
    });
    try {
      await panel.getByRole("button", { name: "Copy Registration ID", exact: true }).focus();
      await lookupPage.keyboard.press("Enter");
      await expect(technical.getByRole("status")).toHaveText("Registration ID copied.");
      assert.equal(await lookupPage.evaluate(() => globalThis.__cisInstanceStatusCopied), adoptionObservations[1]);
    } finally {
      await lookupPage.evaluate(() => {
        if (globalThis.__cisInstanceStatusClipboard === undefined) { delete navigator.clipboard; }
        else { Object.defineProperty(navigator, "clipboard", globalThis.__cisInstanceStatusClipboard); }
        delete globalThis.__cisInstanceStatusClipboard;
        delete globalThis.__cisInstanceStatusCopied;
      });
    }
    await lookupPage.keyboard.press("Escape");
    await expect(panel).toHaveCount(0);
    await expect(faultedButton).toBeFocused();
    await faultedButton.click();
    await expect(lookupPage.getByRole("dialog")).toBeVisible();
    await lookupPage.getByRole("button", { name: "Close details", exact: true }).click();
    await expect(lookupPage.getByRole("dialog")).toHaveCount(0);
    await expect(faultedButton).toBeFocused();
    cases.push("native-instance-status-collapsed-diagnostics-history-keyboard-escape-close-focus", "controlled-instance-status-copy-keeps-exact-reported-registration-without-os-clipboard");
    await screenshot(lookupPage, "instance-status-adoption-only-native");
    assert.deepEqual(await get(page, mapUrl), before, "The read-only view must retain the complete real native assignment map.");
    assert.deepEqual(mutations, [], "Inspecting status, history, copy and Close/Escape must send no mutations.");
    cases.push("native-instance-status-inspection-sends-no-mutation-and-preserves-exact-current-map");
    return { applicationId: applicationA, bindingId: before.bindingId, observations: adoptionObservations, mutations: mutations.length,
      adoptionOnlyMetadataUnavailable: true, reportedVersionDistinctFromDesired: true };
  } finally {
    page.off("request", observe);
    lookupPage.off("request", observe);
  }
}

async function get(page, url) {
  const response = await page.request.get(url);
  try {
    assert.equal(response.status(), 200);
    const reply = await response.json();
    assert.equal(reply.state, "ready");
    return reply.value;
  } finally { await response.dispose(); }
}
async function refused(page, url) {
  const response = await page.request.get(url);
  try {
    assert.ok([403, 404].includes(response.status()));
    assert.equal("value" in await response.json(), false);
  } finally { await response.dispose(); }
}
async function navigate(page, url, observationsUrl) {
  const [response] = await Promise.all([
    page.waitForResponse(value => value.url() === observationsUrl && value.request().method() === "GET"),
    page.goto(url)
  ]);
  assert.equal(response.status(), 200);
  const reply = await response.json();
  assert.equal(reply.state, "ready");
  return reply.value;
}
function seedStatus(row) {
  if (row.registrationFreshness !== "fresh" || row.adoptionFreshness !== "fresh") { return "Unknown"; }
  if (row.convergence === "faulted" && row.reported?.state === "Faulted") { return "Failed"; }
  return row.convergence === "pending" ? "Pending" : "Unknown";
}
