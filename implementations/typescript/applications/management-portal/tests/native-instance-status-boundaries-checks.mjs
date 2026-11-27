// Additional status qualification through the ordinary native host and approved three-column UI.
import assert from "node:assert/strict";
import { expect } from "@playwright/test";
import { configurationRoute } from "../src/portal-routes.ts";
import { portalContextKey } from "../src/portal-context.ts";

export async function verifyInstanceStatusBoundariesBrowser({ page, origin, applicationA, applicationB, instanceStatusBoundaries: seed, screenshot, cases }) {
  const base = origin + `/api/portal/applications/${applicationA}/configurations/shared.settings`;
  const observationsUrl = base + "/adoption?schemaVersion=1&pageSize=20";
  const mapUrl = base + "/assignments?schemaVersion=1";
  const location = origin + configurationRoute({ applicationId: applicationA, applicationName: "managed-a",
    configurationId: "shared.settings", schemaVersion: "1", view: "adoption" });
  const before = await get(page, mapUrl);
  const metadata = (await get(page, base + "/releases?schemaVersion=1&pageSize=100")).value;
  const baseline = metadata.items.find(release => release.releaseId === seed.baselineReleaseId);
  const candidate = metadata.items.find(release => release.releaseId === seed.candidateReleaseId);
  assert.ok(baseline?.reason && candidate?.reason);
  assert.equal(before.value.defaultReleaseId, seed.baselineReleaseId);
  assert.equal(before.value.generation, seed.generation);
  const nativePages = [];
  let cursor;
  do {
    const reply = await get(page, observationsUrl + (cursor === undefined ? "" : "&cursor=" + encodeURIComponent(cursor)));
    assert.equal(portalContextKey(reply.context), portalContextKey(before.context));
    nativePages.push(reply.value);
    cursor = reply.value.nextCursor;
    assert.ok(nativePages.length <= 2, "This exact fixture must stay within two bounded native pages.");
  } while (cursor !== undefined);
  assert.equal(nativePages.length, 2);
  const nativeRows = nativePages.flatMap(value => value.items);
  const expected = new Map(seed.rows.map(row => [row.registrationId, row]));
  for (const row of seed.rows) {
    assertSeededObservation(nativeRows.find(item => item.registrationId === row.registrationId), row);
  }
  const stale = nativeRows.find(row => row.registrationId === seed.staleRegistrationId);
  assert.equal(stale?.registrationFreshness, "stale");
  assert.equal(stale?.adoptionFreshness, "stale");
  assert.ok(nativeRows.some(row => row.convergence === "noInstances"));
  const mutations = [];
  const observe = request => {
    if (new URL(request.url()).origin === origin && ["POST", "PUT", "PATCH", "DELETE"].includes(request.method())) {
      mutations.push(request.method() + " " + new URL(request.url()).pathname);
    }
  };
  page.on("request", observe);
  try {
    let current = await readAfter(page, observationsUrl, () => page.goto(location));
    let currentIndex = 0;
    const seen = new Set();
    for (let index = 0; index < nativePages.length; index++) {
      assert.deepEqual(current.value.items.map(row => row.registrationId), nativePages[index].items.map(row => row.registrationId));
      const table = page.locator(".configuration-instance-status table");
      await expect(table.getByRole("columnheader")).toHaveText(["Instance", "Configuration version in use", "Status"]);
      await expect(table.locator("tbody tr")).toHaveCount(current.value.items.filter(row => row.registrationId !== undefined).length);
      for (const actual of current.value.items.filter(row => row.registrationId !== undefined)) {
        assert.equal(seen.has(actual.registrationId), false, "Actual UI continuation pages must neither overlap nor omit registrations.");
        seen.add(actual.registrationId);
        const seeded = expected.get(actual.registrationId);
        if (seeded !== undefined) { assertSeededObservation(actual, seeded); }
        if (actual.registrationId === seed.staleRegistrationId) {
          assert.equal(actual.registrationFreshness, "stale");
          assert.equal(actual.adoptionFreshness, "stale");
        }
        const row = table.getByRole("row").filter({ has: page.getByRole("button", { name: actual.reportedInstanceId, exact: true }) });
        const label = expected.get(actual.registrationId)?.status ?? "Unknown";
        await expect(row.getByRole("cell").nth(2).locator(".badge")).toHaveText(label);
        if (actual.registrationId === seed.staleRegistrationId) { await expect(row).toContainText("Stale report"); }
        if (actual.reportedInstanceId === "status-offline") { await expect(row).toContainText("Instance offline"); }
        await expect(row).not.toContainText(actual.registrationId);
      }
      const diagnostics = page.locator(".instance-status-targets details");
      await expect(diagnostics).toHaveCount(current.value.items.some(row => row.registrationId === undefined) ? 1 : 0);
      if (current.value.items.some(row => row.registrationId === undefined)) { await expect(diagnostics).not.toHaveAttribute("open", ""); }
      const pager = page.getByRole("navigation").filter({ has: page.getByRole("button", { name: "Previous", exact: true }) });
      if (index === 0) {
        await expect(pager.getByRole("button", { name: "Previous", exact: true })).toBeDisabled();
        current = await readAfter(page, observationsUrl + "&cursor=" + encodeURIComponent(current.value.nextCursor),
          () => pager.getByRole("button", { name: "Next", exact: true }).click());
        currentIndex = 1;
      } else {
        await expect(pager.getByRole("button", { name: "Previous", exact: true })).toBeEnabled();
        await expect(pager.getByRole("button", { name: "Next", exact: true })).toBeDisabled();
      }
    }
    assert.ok(seed.rows.every(row => seen.has(row.registrationId)));
    cases.push("native-instance-status-two-bounded-pages-preserve-fresh-applied-pending-restart-rejected-faulted-stale-offline-and-no-recipient-facts");
    await screenshot(page, "instance-status-mixed-boundaries-native");

    // Locate one real rejected report irrespective of UUID ordering across the two pages.
    if (!current.value.items.some(row => row.reportedInstanceId === "status-rejected")) {
      current = await readAfter(page, observationsUrl, () => page.getByRole("button", { name: "Previous", exact: true }).click());
      currentIndex = 0;
    }
    await page.getByRole("button", { name: "status-rejected", exact: true }).click();
    const panel = page.getByRole("dialog");
    await expect(panel).toContainText(candidate.reason);
    await expect(panel).toContainText(baseline.reason);
    await expect(panel.locator(".badge")).toHaveText("Failed");
    const details = panel.locator("details.technical-details");
    await expect(details).not.toHaveAttribute("open", "");
    await details.locator("summary").click();
    await expect(field(panel, "Candidate failure code")).toContainText("Configuration.Validation");
    await expect(field(panel, "Accepted release ID")).toContainText(seed.candidateReleaseId);
    await expect(field(panel, "Desired release ID")).toContainText(seed.baselineReleaseId);
    await expect(field(panel, "Historical successful release ID")).toContainText(seed.candidateReleaseId);
    cases.push("native-instance-status-keeps-real-current-map-desired-version-separate-from-current-and-historical-acceptance");

    // The approved details panel is modal. Dismiss it through the user's actual
    // Escape interaction before using the underlying Refresh control.
    await page.keyboard.press("Escape");
    await expect(panel).toHaveCount(0);
    const selectedUrl = currentIndex === 0 ? observationsUrl : observationsUrl + "&cursor=" + encodeURIComponent(nativePages[0].nextCursor);
    const refreshed = await readAfter(page, selectedUrl, () => page.getByRole("button", { name: "Refresh", exact: true }).click());
    assert.equal(portalContextKey(refreshed.context), portalContextKey(before.context));
    await expect(page.getByRole("dialog")).toHaveCount(0);
    cases.push("native-instance-status-same-session-refresh-keeps-dismissed-details-closed");

    // Only delivery is controlled. Each candidate begins with a current actual native
    // response and its real session context; server state and grants stay untouched.
    await withReplyBoundary(page, selectedUrl, async (route, response, reply) => {
      await route.fulfill({ response, status: 503, json: { state: "unavailable", context: reply.context, message: "Controlled status delivery failure." } });
    }, async () => {
      await page.getByRole("button", { name: "Refresh", exact: true }).click();
      await expect(page.getByRole("status").filter({ hasText: "Showing an earlier observation" })).toBeVisible();
      await expect(page.locator(".configuration-instance-status .badge").filter({ hasText: /^(Up to date|Failed|Needs restart)$/u })).toHaveCount(0);
      await expect(page.locator(".configuration-instance-status .badge")).toHaveText(refreshed.value.items.filter(row => row.registrationId !== undefined).map(() => "Unknown"));
    });
    await readAfter(page, selectedUrl, () => page.getByRole("button", { name: "Refresh", exact: true }).click());
    cases.push("controlled-instance-status-unavailable-same-session-read-retains-only-explicit-earlier-unknown-observations");

    await delayedReadAfterNavigation(page, selectedUrl, async () => {
      const foreign = configurationRoute({ applicationId: applicationB, applicationName: "managed-b",
        configurationId: "shared.settings", schemaVersion: "1", view: "adoption" });
      await page.goto(origin + foreign);
      await expect(page).toHaveURL(url => url.searchParams.get("applicationId") === applicationB);
      await expect(page.getByRole("alert").filter({ hasText: /Access unavailable|Item unavailable/u })).toBeVisible();
    });
    await expect(page.locator(".configuration-instance-status")).toHaveCount(0);
    await expect(page.getByRole("dialog")).toHaveCount(0);
    for (const row of seed.rows) { await expect(page.getByRole("button", { name: row.name, exact: true })).toHaveCount(0); }
    cases.push("controlled-instance-status-late-actual-native-read-cannot-repopulate-a-different-unauthorized-application");

    await readAfter(page, observationsUrl, () => page.goto(location));
    await withReplyBoundary(page, observationsUrl, async (route, response) => {
      await route.fulfill({ response, status: 401, json: { state: "unauthenticated", message: "Controlled browser session invalidation." } });
    }, async () => {
      await page.getByRole("button", { name: "Refresh", exact: true }).click();
      await expect(page.getByRole("alert").filter({ hasText: "Sign in required" })).toBeVisible();
      await expect(page.locator(".configuration-instance-status")).toHaveCount(0);
      await expect(page.getByRole("dialog")).toHaveCount(0);
    });
    cases.push("controlled-instance-status-session-invalidation-removes-observations-and-details");
    assert.deepEqual((await get(page, mapUrl)).value, before.value);
    assert.deepEqual(mutations, []);
    return { applicationId: applicationA, pages: nativePages.length, registrations: seed.rows.map(row => row.registrationId), mutations: 0,
      mixedStates: true, staleOfflineAndNoInstances: true, desiredAndHistoryRemainSeparate: true, sameSessionRefreshKeepsDetailsClosed: true,
      lateReadCannotRepopulateAnotherApplication: true };
  } finally { page.off("request", observe); }
}

function field(panel, label) { return panel.locator(".technical-field").filter({ has: panel.page().getByText(label, { exact: true }) }).locator("dd"); }
function assertSeededObservation(actual, seeded) {
  assert.ok(actual, "Every admitted named observation must be present in the actual response.");
  assert.equal(actual.reported?.state, seeded.state);
  assert.equal(actual.registrationFreshness, seeded.name === "status-offline" ? "offline" : "fresh");
  assert.equal(actual.adoptionFreshness, "fresh");
}

async function get(page, url) {
  const response = await page.request.get(url);
  try { assert.equal(response.status(), 200); const reply = await response.json(); assert.equal(reply.state, "ready"); return reply; }
  finally { await response.dispose(); }
}

async function readAfter(page, url, action) {
  const response = page.waitForResponse(value => value.url() === url && value.request().method() === "GET", { timeout: 15000 })
    .then(async value => { assert.equal(value.status(), 200); const reply = await value.json(); assert.equal(reply.state, "ready"); return reply; });
  const outcomes = await Promise.allSettled([response, action()]);
  for (const outcome of outcomes) { if (outcome.status === "rejected") { throw outcome.reason; } }
  return outcomes[0].value;
}

async function withReplyBoundary(page, url, deliver, action) {
  let entered = false, failure, finish;
  const joined = new Promise(resolve => { finish = resolve; });
  const handler = async route => {
    if (route.request().method() !== "GET" || entered) {
      try { await route.continue(); } catch (error) { failure ??= error; }
      return;
    }
    entered = true;
    let response;
    try {
      response = await route.fetch({ timeout: 15000 });
      const reply = await response.json();
      assert.equal(response.status(), 200); assert.equal(reply.state, "ready");
      await deliver(route, response, reply);
    } catch (error) { failure = error; try { await route.abort("failed"); } catch { /* Preserve the owned response failure. */ } }
    finally {
      try { if (response !== undefined) { await response.dispose(); } }
      catch (error) { failure ??= error; }
      finally { finish(); }
    }
  };
  await page.route(url, handler);
  try { await action(); await expect.poll(() => entered).toBe(true); await joined; if (failure !== undefined) { throw failure; } }
  finally { if (entered) { await joined; } await page.unroute(url, handler); }
}

async function delayedReadAfterNavigation(page, url, navigate) {
  let entered = false, ready = false, release, finish, failure;
  const released = new Promise(resolve => { release = resolve; });
  const joined = new Promise(resolve => { finish = resolve; });
  const handler = async route => {
    if (route.request().method() !== "GET" || entered) {
      try { await route.continue(); } catch (error) { failure ??= error; }
      return;
    }
    entered = true;
    let response;
    try {
      response = await route.fetch({ timeout: 15000 });
      const reply = await response.json();
      assert.equal(response.status(), 200); assert.equal(reply.state, "ready");
      ready = true;
      await released;
      try { await route.fulfill({ response }); }
      catch (error) {
        if (route.request().failure()?.errorText !== "net::ERR_ABORTED") { throw error; }
      }
    } catch (error) { failure = error; try { await route.abort("failed"); } catch { /* Preserve the first read failure. */ } }
    finally {
      try { if (response !== undefined) { await response.dispose(); } }
      catch (error) { failure ??= error; }
      finally { finish(); }
    }
  };
  await page.route(url, handler);
  try {
    await page.getByRole("button", { name: "Refresh", exact: true }).click();
    await expect.poll(() => ready).toBe(true);
    await navigate();
    release();
    await joined;
    if (failure !== undefined) { throw failure; }
  } finally { release(); if (entered) { await joined; } await page.unroute(url, handler); }
}
