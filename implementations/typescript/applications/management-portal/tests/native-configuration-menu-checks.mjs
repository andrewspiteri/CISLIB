// Actual bounded Configuration inventory and menu-reader lifetime on the ordinary host.
import assert from "node:assert/strict";
import { expect } from "@playwright/test";
import { applicationRoute } from "../src/portal-routes.ts";

export async function verifyConfigurationMenusBrowser({ page, origin, applicationA, configurationMenus: seed, screenshot, cases }) {
  assert.equal(seed.applicationId, applicationA);
  assert.equal(seed.rows.length, 20);
  const base = `/api/portal/applications/${applicationA}`;
  const reads = [];
  const mutations = [];
  const observe = request => {
    const url = new URL(request.url());
    if (url.origin !== origin) { return; }
    if (["POST", "PUT", "PATCH", "DELETE"].includes(request.method())) { mutations.push(request.method() + " " + url.pathname); }
    if (request.method() === "GET" && url.pathname.startsWith(base + "/configurations/") && url.pathname.endsWith("/row-actions")) {
      reads.push(decodeURIComponent(url.pathname.split("/").at(-2)));
    }
  };
  page.on("request", observe);
  try {
    const summary = await readAfter(page, response => response.request().method() === "GET"
      && new URL(response.url()).pathname === base + "/configuration-summaries",
    () => page.goto(origin + applicationRoute({ applicationId: applicationA, applicationName: "managed-a", tab: "configuration" })));
    assert.equal(summary.value.items.length, 20);
    assert.equal(summary.value.nextCursor, undefined);
    assert.deepEqual(summary.value.items.map(row => row.configurationId).sort(), seed.rows.map(row => row.configurationId).sort());
    for (const row of seed.rows) {
      const actual = summary.value.items.find(item => item.configurationId === row.configurationId);
      assert.equal(actual.applicationId, applicationA);
      assert.equal(actual.schemaVersion, row.schemaVersion);
      assert.equal(actual.bindingId, row.bindingId);
      assert.equal(actual.sourceId, row.sourceId);
    }
    const table = page.locator(".configuration-table-frame table");
    await expect(table.locator("tbody tr")).toHaveCount(20);
    await expect(page.getByRole("tab", { name: "Configuration", exact: true })).toHaveAttribute("aria-selected", "true");
    await expect(page.locator(".configuration-row-menu-items")).toHaveCount(0);
    assert.equal(reads.length, 0, "Closed row menus must mount no protected readers and send no action reads.");
    const closedMenuReads = reads.length;

    for (let cycle = 0; cycle < 2; cycle++) {
      for (const row of seed.rows) {
        const trigger = page.getByRole("button", { name: "Actions for " + row.configurationId, exact: true });
        const path = `${base}/configurations/${encodeURIComponent(row.configurationId)}/row-actions`;
        const reply = await readAfter(page, response => response.request().method() === "GET"
          && new URL(response.url()).pathname === path && new URL(response.url()).searchParams.get("schemaVersion") === row.schemaVersion,
        // An open popup can legitimately cover another row's mouse target.
        // The second pass exercises ordinary keyboard activation to switch
        // directly between open menus, without a forced or synthetic click.
        () => cycle === 0 ? trigger.click() : trigger.press("Enter"));
        assert.equal(reply.value.applicationId, applicationA);
        assert.equal(reply.value.configurationId, row.configurationId);
        assert.equal(reply.value.schemaVersion, row.schemaVersion);
        assert.equal(reply.value.bindingId, row.bindingId);
        assert.equal(reply.value.actions.state, "available");
        await expect(trigger).toHaveAttribute("aria-expanded", "true");
        await expect(page.locator(".configuration-row-menu-items")).toHaveCount(1);
        await expect(page.locator(".configuration-row-menu-items").getByRole("button", { name: "View JSON", exact: true })).toBeVisible();
        if (cycle === 0) {
          await page.keyboard.press("Escape");
          await expect(trigger).toHaveAttribute("aria-expanded", "false");
          await expect(trigger).toBeFocused();
          await expect(page.locator(".configuration-row-menu-items")).toHaveCount(0);
        }
      }
    }
    // The second pass switches directly between open menus. Filtering then
    // retires the last one without a route change or a replacement reader.
    const filter = page.getByLabel("Filter configurations on this page", { exact: true });
    const beforeFilter = reads.length;
    await filter.fill(seed.rows[0].configurationId);
    await expect(table.locator("tbody tr")).toHaveCount(1);
    await expect(page.locator(".configuration-row-menu-items")).toHaveCount(0);
    await filter.fill("");
    await expect(table.locator("tbody tr")).toHaveCount(20);
    assert.equal(reads.length, beforeFilter, "Filtering/closing must not issue row-action reads.");
    const first = seed.rows[0];
    const finalTrigger = page.getByRole("button", { name: "Actions for " + first.configurationId, exact: true });
    await readAfter(page, response => response.request().method() === "GET"
      && new URL(response.url()).pathname === `${base}/configurations/${encodeURIComponent(first.configurationId)}/row-actions`,
    () => finalTrigger.click());
    await expect(page.locator(".configuration-row-menu-items")).toHaveCount(1);
    await page.keyboard.press("Escape");
    await expect(finalTrigger).toBeFocused();
    await expect(page.locator(".configuration-row-menu-items")).toHaveCount(0);
    assert.deepEqual(reads, [...seed.rows.map(row => row.configurationId), ...seed.rows.map(row => row.configurationId), first.configurationId]);
    assert.deepEqual(mutations, []);
    await screenshot(page, "configuration-twenty-native-entries-after-menu-reader-retirement");
    cases.push("native-twenty-configuration-rows-render-without-closed-menu-readers",
      "native-forty-one-scoped-menu-reads-retire-across-escape-switch-and-filter-without-effects");
    return { applicationId: applicationA, configurationIds: seed.rows.map(row => row.configurationId), closedMenuReads,
      mutations: mutations.length, cycles: 2, rowReadCount: reads.length, readConfigurationIds: reads,
      filterRestored: true, escapeReturnsFocus: true };
  } finally { page.off("request", observe); }
}

async function readAfter(page, predicate, action) {
  const response = page.waitForResponse(predicate, { timeout: 15000 }).then(async value => {
    assert.equal(value.status(), 200);
    const body = await value.json();
    assert.equal(body.state, "ready");
    return body;
  });
  const results = await Promise.allSettled([response, Promise.resolve().then(action)]);
  for (const result of results) { if (result.status === "rejected") { throw result.reason; } }
  return results[0].value;
}
