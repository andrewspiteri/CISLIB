// Readable creation and exact original recovery through the current versions/Save/Deploy UI.
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
  await expect(page.getByRole("heading", { name: "shared.settings", exact: true })).toBeVisible();
  const deep = page.url();
  assert.equal(new URL(deep).searchParams.get("configurationId"), "shared.settings");
  await expect(page.getByRole("region", { name: "Saved versions", exact: true })).toBeVisible();
  await page.goBack(); await expect(row).toBeVisible();
  await page.goForward(); await expect(page.getByRole("heading", { name: "shared.settings", exact: true })).toBeVisible();
  await page.reload(); await expect(page).toHaveURL(deep);
  await expect(page.getByRole("heading", { name: "shared.settings", exact: true })).toBeVisible();
  await page.getByRole("button", { name: "Configuration", exact: true }).click();
  await expect(row).toBeVisible();
  cases.push("native-readable-tabs-row-actions-version-deep-link-back-forward-and-reload");

  await page.getByRole("link", { name: "New configuration", exact: true }).click();
  await expect(page.getByRole("heading", { name: "New configuration", exact: true })).toBeVisible();
  await page.getByLabel("Configuration name", { exact: true }).fill(configurationId);
  await page.getByLabel("Application JSON", { exact: true }).fill(raw);
  const note = "R08 explicit native creation";
  await page.getByLabel("Version note", { exact: true }).fill(note);
  const creationPath = origin + `/api/portal/applications/${applicationA}/configuration-creations`;
  const creation = await commitConfigurationStage(page, creationPath, "POST", "Save version", "management.configuration.create", true,
    body => assert.deepEqual(body, { commandVersion: 1, configurationId, intent: "createGeneric", reason: note }));
  await recoverConfigurationStage(page, origin + `/api/portal/applications/${applicationA}/configuration-creation-operations/${creation.key}`,
    "Recover original Save result", creation.key);
  await expect(page.getByLabel("Application JSON", { exact: true })).toHaveValue(raw);
  await assertReceiptStorage(page);
  await screenshot(page, "readable-creation-original-recovered-native");
  const sourceId = creation.decision.sourceId, bindingId = creation.decision.bindingId;
  const base = origin + `/api/portal/applications/${applicationA}/configurations/${configurationId}`;
  const publication = await commitConfigurationStage(page, origin + `/api/portal/configuration/sources/${sourceId}/publications`, "POST",
    "Complete incomplete Save", "management.configuration.source.publish", true, body => assert.equal(body.payloadJson, raw));
  await recoverConfigurationStage(page, origin + `/api/portal/configuration/sources/${sourceId}/publication-operations/${publication.key}`,
    "Recover original Save result", publication.key);
  const release = await commitConfigurationStage(page, base + "/releases?schemaVersion=1", "POST", "Continue incomplete Save",
    "management.configuration.release.create", false, body => {
      assert.deepEqual(body.commonSource, { state: "absent" });
      assert.deepEqual(body.applicationSource, { state: "present", publicationId: publication.decision.publicationId });
    });
  const versions = page.getByRole("region", { name: "Saved versions", exact: true });
  const savedRow = versions.locator(`[data-version-id="${release.decision.releaseId}"]`);
  await expect(savedRow.getByRole("button", { name: note, exact: true })).toBeVisible();
  const beforeAssignment = await get(page, base + "/assignments?schemaVersion=1");
  assert.equal(beforeAssignment.defaultReleaseId, undefined); assert.equal(beforeAssignment.generation, undefined); assert.deepEqual(beforeAssignment.overrides, []);
  await expect(page.getByText("Not deployed", { exact: true })).toBeVisible();
  await savedRow.getByRole("button", { name: "Deploy", exact: true }).click();
  const modal = page.getByRole("dialog");
  await expect(modal.getByRole("heading", { name: "Deploy " + note, exact: true })).toBeVisible();
  await modal.getByLabel("Deployment note", { exact: true }).fill("R08 explicitly assign the saved version");
  const assignment = await commitConfigurationStage(page, base + "/assignments?schemaVersion=1", "PUT", "Deploy " + note,
    "management.configuration.assignment.replace", false, body => {
      assert.equal(body.defaultReleaseId, release.decision.releaseId); assert.deepEqual(body.overrides, []); assert.equal(body.intent, "replace");
    });
  assert.equal(assignment.decision.generation, "1");
  await expect(modal).not.toBeVisible();
  await expect(savedRow).toContainText("Deployed");
  const detailUrl = origin + configurationRoute({ applicationId: applicationA, applicationName: "older-cosmetic-label", configurationId, schemaVersion: "1",
    releaseId: release.decision.releaseId });
  await page.goto(detailUrl);
  await expect(page.getByRole("heading", { name: configurationId, exact: true })).toBeVisible();
  await expect(page.locator(".configuration-json-readonly")).toContainText("9007199254740993");
  const detail = await get(page, base + "/details?schemaVersion=1");
  assert.equal(detail.origin, "applicationOnly"); assert.equal(detail.defaultReleaseId, release.decision.releaseId);
  assert.equal(detail.content.layer, "application"); assert.equal(detail.content.publication.publicationId, publication.decision.publicationId);
  assert.equal(detail.content.publication.payloadJson, raw);
  await page.reload(); await expect(page.locator(".configuration-json-readonly")).toContainText("日本語");
  await screenshot(page, "readable-deployed-application-json-native");
  const finalMap = await get(page, base + "/assignments?schemaVersion=1");
  assert.equal(finalMap.generation, "1"); assert.equal(finalMap.defaultReleaseId, release.decision.releaseId); assert.deepEqual(finalMap.overrides, []);
  assert.equal(detail.content.publication.sha256, createHash("sha256").update(raw, "utf8").digest("hex"));
  await assertReceiptStorage(page);
  cases.push("native-readable-save-recovers-actual-setup-and-publication-delivery-loss-with-their-original-keys",
    "native-save-keeps-setup-publication-release-distinct-without-deployment", "native-one-confirmation-deploy-and-exact-readable-version-json");

  const forbidden = await pageB.request.get(origin + `/api/portal/applications/${applicationA}/configuration-creations/context`);
  try { assert.ok([403, 404].includes(forbidden.status())); } finally { await forbidden.dispose(); }
  const protectedPublication = await pageB.request.get(base + "/details?schemaVersion=1");
  try { assert.ok([403, 404].includes(protectedPublication.status())); assert.equal((await protectedPublication.text()).includes("9007199254740993"), false); }
  finally { await protectedPublication.dispose(); }
  await pageB.goto(origin + applicationRoute({ applicationId: applicationB, applicationName: "managed-b", tab: "configuration" }));
  const readonlyRow = pageB.getByRole("row").filter({ has: pageB.getByRole("button", { name: "shared.settings", exact: true }) });
  const readonlyResponse = pageB.waitForResponse(response => response.url() === origin
    + `/api/portal/applications/${applicationB}/configurations/shared.settings/row-actions?schemaVersion=1` && response.request().method() === "GET");
  await readonlyRow.getByRole("button", { name: "Actions for shared.settings", exact: true }).click();
  const readonlyActions = await value(await readonlyResponse);
  assert.equal(readonlyActions.applicationId, applicationB); assert.equal(readonlyActions.configurationId, "shared.settings");
  assert.equal(readonlyActions.actions.state, "available");
  assert.equal(readonlyActions.actions.application.actions.some(action => action.operationId === "publishConfigurationSource" && action.allowed), false);
  assert.equal(readonlyActions.actions.binding.actions.some(action => action.operationId === "replaceConfigurationAssignments" && action.allowed), false);
  for (const label of ["Edit unavailable", "Publish unavailable", "Assign unavailable"]) { await expect(readonlyRow.getByText(label, { exact: true })).toBeVisible(); }
  for (const label of ["Edit", "Publish…", "Assign…"]) { await expect(readonlyRow.getByRole("link", { name: label, exact: true })).toHaveCount(0); }
  await pageB.getByRole("link", { name: "New configuration", exact: true }).click();
  await expect(pageB.getByRole("heading", { name: "New configuration", exact: true })).toBeVisible();
  await expect(pageB.getByLabel("Configuration name", { exact: true })).toHaveCount(0);
  await expect(pageB.getByRole("button", { name: "Save version", exact: true })).toHaveCount(0);
  cases.push("native-independent-readonly-operator-denied-create-publish-assignment-and-content");
  return { applicationId: applicationA, configurationId, schemaVersion: "1", sourceId, bindingId,
    creationKey: creation.key, publicationKey: publication.key, releaseKey: release.key, assignmentKey: assignment.key,
    publicationId: publication.decision.publicationId, releaseId: release.decision.releaseId, draftSha256: detail.content.publication.sha256 };
}

async function value(response) { assert.equal(response.status(), 200); const reply = await response.json(); assert.equal(reply.state, "ready"); return reply.value; }
async function get(page, url) { const response = await page.request.get(url); try { return await value(response); } finally { await response.dispose(); } }
async function assertReceiptStorage(page) {
  const stored = await page.evaluate(() => sessionStorage.getItem("cis.management.command-receipts.v1"));
  if (stored === null) { return; }
  for (const forbidden of ["9007199254740993", "日本語", '"payloadJson"', '"reason"', '"eTag"', '"defaultsJson"', '"accessToken"']) {
    assert.equal(stored.includes(forbidden), false, "Receipt storage contains identities only, never JSON or credentials.");
  }
}
export async function recoverConfigurationStage(page, url, button, key) {
  const response = page.waitForResponse(reply => reply.url() === url && reply.request().method() === "GET");
  const outcomes = await Promise.allSettled([response.then(async reply => {
    const recovered = await value(reply);
    assert.equal(recovered.key, key); assert.equal(recovered.resultSource, "found"); assert.equal(recovered.originalCompletion, "committed");
  }), page.getByRole("button", { name: button, exact: true }).click()]);
  let failure;
  for (const outcome of outcomes) { if (outcome.status === "rejected") { failure = combine(failure, outcome.reason); } }
  if (failure !== undefined) { throw failure; }
}
export async function commitConfigurationStage(page, url, method, button, family, drop, checkBody) {
  let entered = false, result, failure, settle;
  const joined = new Promise(resolve => { settle = resolve; });
  const handler = async route => {
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
    } catch (error) { failure = combine(failure, error); try { await route.abort("failed"); } catch (cleanup) { failure = combine(failure, cleanup); } }
    finally {
      try { if (response !== undefined) { await response.dispose(); } } catch (error) { failure = combine(failure, error); }
      finally { settle(); }
    }
  };
  await page.route(url, handler);
  let original;
  try {
    await page.getByRole("button", { name: button, exact: true }).click();
    await expect.poll(() => entered, { timeout: 10000 }).toBe(true);
    await joined; if (failure !== undefined) { throw failure; }
    await expect(page.getByRole("status").filter({ hasText: /Waiting for the current/u })).toHaveCount(0);
  } catch (error) { original = error; }
  finally {
    try { if (entered) { await joined; } }
    finally { try { await page.unroute(url, handler); } catch (error) { original = combine(original, error); } }
  }
  if (failure !== undefined && failure !== original) { original = combine(original, failure); }
  if (original !== undefined) { throw original; }
  return result;
}
function combine(first, next) { return first === undefined ? next : first === next ? first : new AggregateError([first, next], "Native Configuration assertion and owned response cleanup both failed."); }
