// One continuation of the existing native Auth.js/Management fixture. Delivery loss follows a real committed response.
import assert from "node:assert/strict";
import { createHash } from "node:crypto";
import { expect } from "@playwright/test";
import { applicationRoute, configurationRoute } from "../src/portal-routes.ts";

const configurationId = "portal.r08.flags";
const raw = String.raw` { "Large":9007199254740993,"Unicode":"日本語 🌍 e\u0301","Escaped":"\u00e9","Zero":0,"Enabled":false,"Optional":null } `;

export async function verifyR08Browser({ page, pageB, origin, applicationA, applicationB, screenshot, cases }) {
  const list = applicationRoute({ applicationId: applicationA, applicationName: "managed-a", tab: "configuration" });
  await page.goto(origin + applicationRoute({ applicationId: applicationA, applicationName: "managed-a", tab: "instances" }));
  await expect(page.getByRole("tab", { name: "Instances", exact: true })).toHaveAttribute("aria-selected", "true");
  await page.getByRole("tab", { name: "Configuration", exact: true }).click();
  await expect(page).toHaveURL(url => url.pathname.endsWith("/configuration") && url.searchParams.get("applicationId") === applicationA);
  const row = page.getByRole("row").filter({ has: page.getByRole("button", { name: "shared.settings", exact: true }) });
  await row.getByRole("button", { name: "Actions for shared.settings", exact: true }).click();
  await expect(page.getByRole("dialog")).toHaveCount(0);
  await row.getByRole("button", { name: "View JSON", exact: true }).click();
  await expect(page.getByRole("dialog")).toBeVisible();
  const deep = page.url();
  assert.equal(new URL(deep).searchParams.get("configurationId"), "shared.settings");
  await page.keyboard.press("Escape"); await expect(page.getByRole("dialog")).toHaveCount(0);
  await expect(row.getByRole("button", { name: "shared.settings", exact: true })).toBeFocused();
  await page.goBack(); await expect(page.getByRole("dialog")).toBeVisible();
  await page.reload(); await expect(page.getByRole("dialog")).toBeVisible();
  await expect(page).toHaveURL(deep);
  await page.keyboard.press("Escape");
  cases.push("native-r08-readable-tabs-row-actions-detail-back-reload-and-focus");

  await page.getByRole("link", { name: "New configuration", exact: true }).click();
  await expect(page.getByRole("heading", { name: "New configuration", exact: true })).toBeVisible();
  await page.getByLabel("Configuration name", { exact: true }).fill(configurationId);
  await page.getByLabel("Application JSON", { exact: true }).fill(raw);
  await page.getByLabel("Change reason", { exact: true }).fill("R08 explicit native creation");
  await page.getByRole("button", { name: "Review creation", exact: true }).click();
  await expect(page.getByRole("heading", { name: "Review creation", exact: true })).toBeFocused();
  const creationPath = origin + `/api/portal/applications/${applicationA}/configuration-creations`;
  const creation = await commit(page, creationPath, "POST", "Confirm configuration creation", "management.configuration.create", true,
    body => { assert.deepEqual(body, { commandVersion: 1, configurationId, intent: "createGeneric", reason: "R08 explicit native creation" }); });
  const creationLookup = origin + `/api/portal/applications/${applicationA}/configuration-creation-operations/${creation.key}`;
  const recoveredResponse = page.waitForResponse(response => response.url() === creationLookup && response.request().method() === "GET");
  await page.getByRole("button", { name: "Recover original creation result", exact: true }).click();
  const recovered = await value(await recoveredResponse);
  assert.equal(recovered.key, creation.key); assert.equal(recovered.resultSource, "found"); assert.equal(recovered.originalCompletion, "committed");
  await expect(page.getByRole("heading", { name: "Configuration created; JSON still needs publication", exact: true })).toBeVisible();
  await expect(page.getByLabel("Application JSON", { exact: true })).toHaveValue(raw);
  await assertReceiptStorage(page);
  await screenshot(page, "r08-native-creation-original-recovered");
  await page.getByRole("button", { name: "Continue to publication", exact: true }).click();
  await expect(page).toHaveURL(url => url.pathname === "/applications/managed-a/configuration/portal-r08-flags/edit"
    && url.searchParams.get("applicationId") === applicationA && url.searchParams.get("configurationId") === configurationId
    && url.searchParams.get("schemaVersion") === "1" && url.searchParams.get("source") === "application");
  const editor = page.locator("#configuration-raw-json");
  await expect(editor).toHaveValue(raw);
  await page.getByLabel("Change reason", { exact: true }).fill("R08 exact Application JSON publication");
  await page.getByRole("button", { name: "Review Application JSON version", exact: true }).click();
  await page.getByRole("radio", { name: "Use no shared publication · Application only", exact: true }).check();
  await page.getByRole("button", { name: "Continue to JSON version review", exact: true }).click();
  await expect(page.getByRole("heading", { name: "Review Application JSON version", exact: true })).toBeFocused();
  const sourceId = creation.decision.sourceId, bindingId = creation.decision.bindingId;
  const publication = await commit(page, origin + `/api/portal/configuration/sources/${sourceId}/publications`, "POST",
    "Publish Application JSON version", "management.configuration.source.publish", false, body => { assert.equal(body.payloadJson, raw); });
  await expect(page.getByRole("heading", { name: "Publication committed — not assigned", exact: true })).toBeVisible();
  await page.getByRole("button", { name: "Review release with this publication", exact: true }).click();
  await page.locator("#configuration-release-reason").fill("R08 separate coherent release");
  await page.getByRole("button", { name: "Review release", exact: true }).click();
  await expect(page.getByRole("heading", { name: "Review release: Shared + Application", exact: true })).toBeFocused();
  const base = origin + `/api/portal/applications/${applicationA}/configurations/${configurationId}`;
  const release = await commit(page, base + "/releases?schemaVersion=1", "POST", "Create release — not assigned",
    "management.configuration.release.create", false, body => {
      assert.deepEqual(body.commonSource, { state: "absent" });
      assert.deepEqual(body.applicationSource, { state: "present", publicationId: publication.decision.publicationId });
    });
  const beforeAssignment = await value(await page.request.get(base + "/assignments?schemaVersion=1"));
  assert.equal(beforeAssignment.defaultReleaseId, undefined); assert.equal(beforeAssignment.generation, undefined); assert.deepEqual(beforeAssignment.overrides, []);
  let leaveConfirmed = false;
  page.once("dialog", async dialog => { assert.equal(dialog.type(), "confirm"); leaveConfirmed = true; await dialog.accept(); });
  await page.getByRole("button", { name: "Review assignment for this release", exact: true }).click();
  await expect(page.getByRole("heading", { name: "Assignment map", exact: true })).toBeVisible();
  assert.equal(leaveConfirmed, true, "The editor retains its own dirty intent until deliberate navigation.");
  const selected = page.getByRole("region", { name: "Selected exact release", exact: true });
  await expect(selected).toBeVisible();
  await selected.getByRole("button", { name: "Use for application default", exact: true }).click();
  await page.getByLabel("Assignment reason", { exact: true }).fill("R08 explicitly assign the prepared default");
  await page.getByRole("button", { name: "Review complete assignment", exact: true }).click();
  await expect(page.getByRole("heading", { name: "Review assignment map", exact: true })).toBeFocused();
  const assignment = await commit(page, base + "/assignments?schemaVersion=1", "PUT", "Confirm assignment", "management.configuration.assignment.replace", false,
    body => { assert.equal(body.defaultReleaseId, release.decision.releaseId); assert.deepEqual(body.overrides, []); assert.equal(body.intent, "replace"); });
  assert.equal(assignment.decision.generation, "1");
  await page.getByRole("button", { name: "Dismiss resolved assignment and refresh", exact: true }).click();
  const detailUrl = origin + configurationRoute({ applicationId: applicationA, applicationName: "older-cosmetic-label", configurationId, schemaVersion: "1" });
  await page.goto(detailUrl); await expect(page.getByRole("dialog")).toBeVisible();
  await expect(page.locator(".configuration-json-readonly")).toContainText("9007199254740993");
  const detail = await value(await page.request.get(base + "/details?schemaVersion=1"));
  assert.equal(detail.origin, "applicationOnly"); assert.equal(detail.defaultReleaseId, release.decision.releaseId);
  assert.equal(detail.content.layer, "application"); assert.equal(detail.content.publication.publicationId, publication.decision.publicationId);
  assert.equal(detail.content.publication.payloadJson, raw);
  await page.reload(); await expect(page.getByRole("dialog")).toBeVisible();
  await expect(page.locator(".configuration-json-readonly")).toContainText("日本語");
  await screenshot(page, "r08-native-assigned-application-json");
  const finalMap = await value(await page.request.get(base + "/assignments?schemaVersion=1"));
  assert.equal(finalMap.generation, "1"); assert.equal(finalMap.defaultReleaseId, release.decision.releaseId); assert.deepEqual(finalMap.overrides, []);
  assert.equal(detail.content.publication.sha256, createHash("sha256").update(raw, "utf8").digest("hex"));
  await assertReceiptStorage(page);
  cases.push("native-r08-creation-actual-delivery-loss-same-key-recovery", "native-r08-create-then-publish-exact-in-memory-json",
    "native-r08-separate-release-and-explicit-default-assignment", "native-r08-deep-link-exact-assigned-layer-json");

  const forbidden = await pageB.request.get(origin + `/api/portal/applications/${applicationA}/configuration-creations/context`);
  assert.ok([403, 404].includes(forbidden.status()));
  const protectedPublication = await pageB.request.get(origin + `/api/portal/applications/${applicationA}/configurations/${configurationId}/details?schemaVersion=1`);
  assert.ok([403, 404].includes(protectedPublication.status())); assert.equal((await protectedPublication.text()).includes("9007199254740993"), false);
  await pageB.goto(origin + applicationRoute({ applicationId: applicationB, applicationName: "managed-b", tab: "configuration" }));
  const readonlyRow = pageB.getByRole("row").filter({ has: pageB.getByRole("button", { name: "shared.settings", exact: true }) });
  const readonlyActionsResponse = pageB.waitForResponse(response => response.url() === origin
    + `/api/portal/applications/${applicationB}/configurations/shared.settings/row-actions?schemaVersion=1` && response.request().method() === "GET");
  await readonlyRow.getByRole("button", { name: "Actions for shared.settings", exact: true }).click();
  const readonlyActions = await value(await readonlyActionsResponse);
  assert.equal(readonlyActions.applicationId, applicationB); assert.equal(readonlyActions.configurationId, "shared.settings");
  assert.equal(readonlyActions.actions.state, "available");
  assert.equal(readonlyActions.actions.application.actions.some(action => action.operationId === "publishConfigurationSource" && action.allowed), false);
  assert.equal(readonlyActions.actions.binding.actions.some(action => action.operationId === "replaceConfigurationAssignments" && action.allowed), false);
  await expect(readonlyRow.getByText("Edit unavailable", { exact: true })).toBeVisible();
  await expect(readonlyRow.getByText("Publish unavailable", { exact: true })).toBeVisible();
  await expect(readonlyRow.getByText("Assign unavailable", { exact: true })).toBeVisible();
  await expect(readonlyRow.getByRole("link", { name: "Edit", exact: true })).toHaveCount(0);
  await expect(readonlyRow.getByRole("link", { name: "Publish…", exact: true })).toHaveCount(0);
  await expect(readonlyRow.getByRole("link", { name: "Assign…", exact: true })).toHaveCount(0);
  await pageB.getByRole("link", { name: "New configuration", exact: true }).click();
  await expect(pageB.getByRole("heading", { name: "New configuration", exact: true })).toBeVisible();
  await expect(pageB.getByLabel("Configuration name", { exact: true })).toHaveCount(0);
  await expect(pageB.getByRole("button", { name: "Confirm configuration creation", exact: true })).toHaveCount(0);
  cases.push("native-r08-independent-readonly-operator-denied-create-publish-assignment-and-content");
  return { applicationId: applicationA, configurationId, schemaVersion: "1", sourceId, bindingId,
    creationKey: creation.key, publicationKey: publication.key, releaseKey: release.key, assignmentKey: assignment.key,
    publicationId: publication.decision.publicationId, releaseId: release.decision.releaseId, draftSha256: detail.content.publication.sha256 };
}

async function value(response) {
  assert.equal(response.status(), 200); const reply = await response.json(); assert.equal(reply.state, "ready"); return reply.value;
}
async function assertReceiptStorage(page) {
  const stored = await page.evaluate(() => sessionStorage.getItem("cis.management.command-receipts.v1"));
  if (stored === null) { return; }
  for (const forbidden of ["9007199254740993", "日本語", '"payloadJson"', '"reason"', '"eTag"', '"defaultsJson"', '"accessToken"']) {
    assert.equal(stored.includes(forbidden), false, "Receipt storage contains identities only, never JSON or credentials.");
  }
}
async function commit(page, url, method, button, family, drop, checkBody) {
  let entered = false, result, failure, settle;
  const joined = new Promise(resolve => { settle = resolve; });
  await page.route(url, async route => {
    if (route.request().method() !== method) { await route.continue(); return; }
    entered = true; let response;
    try {
      const key = route.request().headers()["idempotency-key"];
      const stored = await page.evaluate(() => sessionStorage.getItem("cis.management.command-receipts.v1"));
      assert.ok(stored?.includes(key), "The original reference must be retained before native dispatch.");
      await assertReceiptStorage(page); checkBody?.(route.request().postDataJSON());
      response = await route.fetch({ timeout: 30000 }); result = await value(response);
      assert.equal(result.family, family); assert.equal(result.key, key); assert.equal(result.disposition, "applied"); assert.equal(result.originalCompletion, "committed");
      if (drop) { await route.abort("failed"); } else { await route.fulfill({ response }); }
    } catch (error) { failure = error; try { await route.abort("failed"); } catch { /* Keep the original fault until owned response cleanup settles. */ } }
    finally {
      try { if (response !== undefined) { await response.dispose(); } }
      catch (error) { failure = failure === undefined ? error : new AggregateError([failure, error], "Native response cleanup failed."); }
      finally { settle(); }
    }
  });
  try {
    await page.getByRole("button", { name: button, exact: true }).click();
    await expect.poll(() => entered, { timeout: 10000 }).toBe(true);
    await joined; if (failure !== undefined) { throw failure; }
    await expect(page.getByRole("status").filter({ hasText: /Waiting for the current/u })).toHaveCount(0);
  } finally { if (entered) { await joined; } await page.unroute(url); }
  return result;
}
