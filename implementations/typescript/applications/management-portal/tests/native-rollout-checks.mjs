// Finite assignment continuation borrowing the existing real Management/OIDC/browser owners.
import assert from "node:assert/strict";
import { randomUUID } from "node:crypto";
import { expect } from "@playwright/test";
import { capturePortalContext, portalContextHeader, portalContextKey } from "../src/portal-context.ts";

export async function verifyRolloutApi({ a, b, lookup, lookupContext, origin, input, contextA, contextB, cases, reportPhase }) {
  const base = `/api/portal/applications/${input.applicationA}/configurations/${input.configurationId}`;
  const query = `schemaVersion=${input.configurationSchema}`;
  reportPhase("rollout-api-current-map-and-grants");
  const map = await value(await a.get(`${base}/assignments?${query}`));
  assert.equal(map.defaultReleaseId, input.rollout.baselineReleaseId);
  assert.deepEqual(map.overrides, [{ cohortId: "finance", releaseId: input.rollout.financeReleaseId }]);
  const actions = await value(await a.get(`${base}/binding-actions?${query}`));
  assert.equal(actions.resourceId, map.bindingId);
  assert.ok(actions.actions.some(action => action.operationId === "replaceConfigurationAssignments" && action.supported && action.allowed));
  reportPhase("rollout-api-retained-selector-pagination");
  const first = await value(await a.get(`${base}/releases?${query}&pageSize=2`));
  assert.equal(first.items.length, 2); assert.ok(first.nextCursor);
  const second = await value(await a.get(`${base}/releases?${query}&pageSize=2&cursor=${encodeURIComponent(first.nextCursor)}`));
  assert.equal(second.items.length, 2); assert.ok(second.items.every(item => !first.items.some(other => other.releaseId === item.releaseId)));
  const candidate = await value(await a.get(`/api/portal/configuration/releases/${input.rollout.candidateReleaseId}`));
  assert.equal(candidate.applicationId, input.applicationA); assert.equal(candidate.environment, map.environment);
  const content = await a.get(`${base}/publication/${candidate.applicationSource.publicationId}?${query}&source=application`);
  assert.ok([403, 404].includes(content.status()), "Map authoring must not depend on protected source ContentRead.");
  const headers = { Origin: origin, "Content-Type": "application/json", [portalContextHeader]: portalContextKey(contextA) };
  const assignment = { commandVersion: 1, intent: "promote", defaultReleaseId: candidate.releaseId, overrides: map.overrides, reason: "Metadata-only eligibility proof" };
  reportPhase("rollout-api-protected-effective-preview-no-effects");
  const preview = await value(await a.post("/api/portal/configuration/preview?pageSize=2", { headers, data: {
    commandVersion: 1, applicationId: input.applicationA, configurationId: input.configurationId, schemaVersion: input.configurationSchema, assignment } }));
  assert.equal(preview.structurallyValid, true); assert.equal(preview.redacted, true);
  assert.deepEqual(preview.currentAssignment, map); assert.deepEqual(preview.proposedAssignment, assignment);
  assert.deepEqual(await value(await a.get(`${base}/assignments?${query}`)), map);
  reportPhase("rollout-api-own-original-with-read-write-removed");
  assert.ok([403, 404].includes((await lookup.get(`${base}/assignments?${query}`)).status()));
  const original = await value(await lookup.get(`${base}/assignment-operations/${input.rollout.assignmentKey}?${query}`,
    { headers: { [portalContextHeader]: portalContextKey(lookupContext) } }));
  assert.equal(original.disposition, "applied"); assert.equal(original.resultSource, "found"); assert.equal(original.key, input.rollout.assignmentKey);
  reportPhase("rollout-api-adoption-without-app-definition-content-reads");
  const observed = await value(await lookup.get(`${base}/adoption?${query}&pageSize=20`));
  const historical = observed.items.find(item => item.registrationId === input.adoptionObservations[0]);
  const faulted = observed.items.find(item => item.registrationId === input.adoptionObservations[1]);
  assert.equal(historical.reported.state, "Applied"); assert.equal(faulted.reported.state, "Faulted");
  assert.notEqual(historical.reported.applied.releaseId, historical.desired.releaseId);
  assert.ok(!["applied", "appliedUnchanged"].includes(historical.convergence));
  assert.equal(faulted.reported.applied, undefined); assert.ok(faulted.reported.lastSuccessful);
  assert.deepEqual(await value(await a.get(`${base}/assignments?${query}`)), map, "Adoption reads must not change the complete map.");
  const denied = await value(await b.put(`/api/portal/applications/${input.applicationB}/configurations/${input.configurationId}/assignments?${query}`, {
    headers: { ...headers, [portalContextHeader]: portalContextKey(contextB), "Idempotency-Key": randomUUID(), "If-Match": map.eTag }, data: assignment }));
  assert.equal(denied.disposition, "unresolved"); assert.ok([403, 404].includes(denied.transport.status));
  cases.push("native-rollout-complete-map-and-current-dual-grants", "native-rollout-bounded-retained-release-pagination",
    "native-rollout-redacted-metadata-preview-without-source-content-authority", "native-rollout-own-lookup-with-read-write-removed",
    "native-adoption-read-alone-preserves-historical-report-facts-without-map-effects");
  reportPhase("rollout-api-complete");
}

export async function verifyRolloutBrowser({ page, pageB, origin, applicationA, applicationB, rollout, screenshot, cases }) {
  const base = origin + `/api/portal/applications/${applicationA}/configurations/shared.settings`;
  const mapPath = base + "/assignments?schemaVersion=1";
  const mapBefore = await value(await page.request.get(mapPath));
  const appliedKeys = [];
  await page.goto(origin + `/applications/${applicationA}?tab=configuration`);
  await page.getByRole("button", { name: "shared.settings", exact: true }).click();
  const details = page.getByRole("dialog");
  await expect(details).toBeVisible();
  await details.getByRole("link", { name: "Manage assignment", exact: true }).focus(); await page.keyboard.press("Enter");
  await expect(page.getByRole("heading", { name: "Assignment map", exact: true })).toBeVisible();
  await expect(page).toHaveURL(url => url.pathname.endsWith("/assignment") && url.searchParams.get("applicationId") === applicationA
    && url.searchParams.get("configurationId") === "shared.settings" && url.searchParams.get("schemaVersion") === "1");
  const pager = page.getByRole("navigation", { name: "retained releases pagination", exact: true });
  await expect(pager.getByRole("button", { name: "Next", exact: true })).toBeEnabled();
  await pager.getByRole("button", { name: "Next", exact: true }).click();
  await expect(pager.getByRole("button", { name: "Previous", exact: true })).toBeEnabled();
  await pager.getByRole("button", { name: "Previous", exact: true }).click();
  await page.getByLabel("Cohort for an override", { exact: true }).selectOption("canary");
  await exactRelease(page, rollout.candidateReleaseId);
  await page.getByRole("region", { name: "Selected exact release", exact: true }).getByRole("button", { name: "Use for selected cohort", exact: true }).click();
  const intended = page.getByRole("table", { name: "Current and proposed entries", exact: true });
  await expect(intended.getByRole("row").filter({ has: page.getByRole("rowheader", { name: "finance", exact: true }) })).toContainText("retained");
  await expect(page.getByRole("button", { name: "Remove finance override", exact: true })).toBeDisabled();
  await page.getByLabel("Assignment reason", { exact: true }).fill("Canary with unrelated Finance retained");
  await review(page);
  await expect(page.getByText("Effective source values are protected.", { exact: false })).toBeVisible();
  const canary = await submit(page, mapPath);
  appliedKeys.push(canary.key); assert.equal(canary.disposition, "applied");
  const canaryMap = await value(await page.request.get(mapPath));
  assert.equal(canaryMap.defaultReleaseId, rollout.baselineReleaseId);
  assert.deepEqual(canaryMap.overrides, [{ cohortId: "canary", releaseId: rollout.candidateReleaseId }, { cohortId: "finance", releaseId: rollout.financeReleaseId }]);
  assert.equal(BigInt(canaryMap.generation), BigInt(mapBefore.generation) + 1n);
  await page.getByRole("button", { name: "Dismiss resolved assignment and refresh", exact: true }).click();
  await expect(page.getByRole("button", { name: "Promote", exact: true })).toBeEnabled();
  await page.getByRole("button", { name: "Promote", exact: true }).click();
  await exactRelease(page, rollout.candidateReleaseId);
  await page.getByRole("region", { name: "Selected exact release", exact: true }).getByRole("button", { name: "Use for application default", exact: true }).click();
  await page.getByRole("button", { name: "Remove canary override", exact: true }).click();
  await page.getByLabel("Assignment reason", { exact: true }).fill("Promote candidate; explicitly remove canary and retain Finance");
  await review(page);
  const affected = page.getByRole("table", { name: "Every affected map entry", exact: true });
  await expect(affected).toContainText("Inherit application default");
  await expect(affected.getByRole("row").filter({ has: page.getByRole("rowheader", { name: "finance", exact: true }) })).toContainText("retained");
  await screenshot(page, "assignment-promotion-review-native");
  const promoted = await dropCommittedAssignment(page, mapPath);
  appliedKeys.push(promoted.key);
  const original = page.getByRole("region", { name: "Assignment original", exact: true });
  await original.getByRole("button", { name: "Recover original assignment", exact: true }).click();
  await expect(original.getByRole("heading", { name: "Assignment committed", exact: true })).toBeVisible();
  await expect(original.getByRole("button", { name: "Recover original assignment", exact: true })).toBeDisabled();
  const promotedMap = await value(await page.request.get(mapPath));
  assert.equal(promotedMap.defaultReleaseId, rollout.candidateReleaseId); assert.deepEqual(promotedMap.overrides, [{ cohortId: "finance", releaseId: rollout.financeReleaseId }]);
  assert.equal(BigInt(promotedMap.generation), BigInt(canaryMap.generation) + 1n);
  await screenshot(page, "assignment-original-recovery-native");
  await original.getByRole("button", { name: "Dismiss resolved assignment and refresh", exact: true }).click();
  await expect(page.getByRole("button", { name: "Rollback", exact: true })).toBeEnabled();
  await page.getByRole("button", { name: "Rollback", exact: true }).click();
  await exactRelease(page, rollout.baselineReleaseId);
  await page.getByRole("region", { name: "Selected exact release", exact: true }).getByRole("button", { name: "Use for application default", exact: true }).click();
  await page.getByLabel("Assignment reason", { exact: true }).fill("Rollback to explicitly retained earlier release");
  await review(page);
  const current = await protectedReply(await page.request.get(mapPath));
  const competingKey = randomUUID();
  const competing = await value(await page.request.put(mapPath, { headers: { Origin: origin, "Content-Type": "application/json", [portalContextHeader]: portalContextKey(current.context),
    "Idempotency-Key": competingKey, "If-Match": current.value.eTag }, data: { commandVersion: 1, intent: "replace", defaultReleaseId: current.value.defaultReleaseId,
      overrides: current.value.overrides, reason: "Real concurrent map decision after review" } }));
  assert.equal(competing.disposition, "applied"); appliedKeys.push(competingKey);
  const conflict = await submit(page, mapPath); assert.equal(conflict.disposition, "conflict");
  await expect(original.getByRole("heading", { name: "Original assignment precondition conflict", exact: true })).toBeVisible();
  await original.getByRole("button", { name: "Dismiss resolved assignment and refresh", exact: true }).click();
  await expect(page.getByRole("heading", { name: "Current assignment changed", exact: true })).toBeVisible();
  let discarded = false;
  page.once("dialog", async dialog => { assert.equal(dialog.type(), "confirm"); discarded = true; await dialog.accept(); });
  await page.getByRole("button", { name: "Use current map and discard intended changes", exact: true }).click(); assert.equal(discarded, true);
  await page.getByRole("button", { name: "Rollback", exact: true }).click();
  await exactRelease(page, rollout.baselineReleaseId);
  await page.getByRole("region", { name: "Selected exact release", exact: true }).getByRole("button", { name: "Use for application default", exact: true }).click();
  await page.getByLabel("Assignment reason", { exact: true }).fill("Deliberate renewed rollback review after concurrent change");
  await review(page);
  const rollback = await submit(page, mapPath); assert.equal(rollback.disposition, "applied"); appliedKeys.push(rollback.key);
  const finalMap = await value(await page.request.get(mapPath));
  assert.equal(finalMap.defaultReleaseId, rollout.baselineReleaseId); assert.deepEqual(finalMap.overrides, [{ cohortId: "finance", releaseId: rollout.financeReleaseId }]);
  assert.equal(BigInt(finalMap.generation), BigInt(mapBefore.generation) + 4n);
  await screenshot(page, "assignment-retained-release-rollback-native");
  await original.getByRole("button", { name: "Dismiss resolved assignment and refresh", exact: true }).click();
  await page.getByLabel("Exact retained release ID", { exact: true }).fill(randomUUID());
  await page.getByRole("button", { name: "Inspect exact release", exact: true }).click();
  await expect(page.getByRole("region", { name: "Selected exact release", exact: true })).toHaveCount(0);
  await expect(page.getByRole("main").getByRole("alert")).toContainText("Item unavailable");
  await pageB.goto(origin + `/applications/${applicationB}/configurations/shared.settings/assignment?schemaVersion=1`);
  await expect(pageB.getByRole("heading", { name: "Assignment map", exact: true })).toBeVisible();
  await expect(pageB.getByRole("button", { name: "Review complete assignment", exact: true })).toBeDisabled();
  await expect(pageB.getByRole("button", { name: "Confirm assignment", exact: true })).toHaveCount(0);
  cases.push("native-rollout-keyboard-canary-retains-unrelated-pin", "native-rollout-explicit-promotion-removes-canary-retains-finance",
    "controlled-rollout-native-commit-delivery-loss-same-key-recovery", "native-rollout-real-concurrent-map-conflict-and-deliberate-reconciliation",
    "native-rollout-retained-release-rollback-advances-generation", "native-rollout-bounded-browser-selection-and-unavailable-exact-release",
    "native-rollout-read-only-user-has-no-confirm-action");
  return { appliedKeys, conflictKey: conflict.key };
}

export async function verifyAdoptionBrowser({ page, lookupPage, origin, applicationA, adoptionObservations, screenshot, cases }) {
  const mapPath = origin + `/api/portal/applications/${applicationA}/configurations/shared.settings/assignments?schemaVersion=1`;
  const before = await value(await page.request.get(mapPath));
  await page.getByRole("link", { name: "View Instance status", exact: true }).focus(); await page.keyboard.press("Enter");
  await expect(page.getByRole("heading", { name: "Instance status", exact: true })).toBeVisible();
  const historicalRow = page.getByRole("row").filter({ has: page.getByRole("button", { name: "adoption-historical", exact: true }) });
  await expect(historicalRow).not.toContainText("Up to date");
  await screenshot(page, "configuration-adoption-historical-mismatch-native");
  await lookupPage.goto(origin + `/applications/${applicationA}/configurations/shared.settings/adoption?schemaVersion=1`);
  await expect(lookupPage.getByRole("heading", { name: "Instance status", exact: true })).toBeVisible();
  const faultedButton = lookupPage.getByRole("button", { name: "adoption-faulted", exact: true });
  await faultedButton.focus(); await lookupPage.keyboard.press("Enter");
  const panel = lookupPage.getByRole("dialog");
  await expect(panel.getByRole("heading", { name: "adoption-faulted", exact: true })).toBeVisible();
  const technical = panel.locator("details.technical-details");
  await expect(technical).not.toHaveAttribute("open", "");
  await technical.locator("summary").focus(); await lookupPage.keyboard.press("Enter");
  await expect(technical.locator(".technical-field").filter({ has: lookupPage.locator("dt").getByText("Reported app version", { exact: true }) }).locator("dd")).toContainText("3.1.0");
  await expect(technical.locator(".technical-field").filter({ has: lookupPage.locator("dt").getByText("Reported state", { exact: true }) }).locator("dd")).toContainText("Faulted");
  await expect(technical.locator(".technical-field").filter({ has: lookupPage.locator("dt").getByText("Candidate failure code", { exact: true }) }).locator("dd")).toContainText("Configuration.LiveSetter");
  await expect(technical.locator(".technical-field").filter({ has: lookupPage.locator("dt").getByText("Historical acceptance meaning", { exact: true }) }).locator("dd")).toContainText("Earlier acceptance does not establish current live values");
  await expect(panel.getByText(adoptionObservations[1], { exact: true })).toBeVisible();
  await lookupPage.evaluate(() => { Object.defineProperty(navigator, "clipboard", { configurable: true, value: { writeText: async text => { globalThis.__cisAdoptionCopied = text; } } }); });
  try {
    await panel.getByRole("button", { name: "Copy Registration ID", exact: true }).focus(); await lookupPage.keyboard.press("Enter");
    await expect(technical.getByRole("status")).toHaveText("Registration ID copied.");
    assert.equal(await lookupPage.evaluate(() => globalThis.__cisAdoptionCopied), adoptionObservations[1]);
  } finally { await lookupPage.evaluate(() => { delete navigator.clipboard; delete globalThis.__cisAdoptionCopied; }); }
  await screenshot(lookupPage, "configuration-adoption-low-authority-details-native");
  await lookupPage.keyboard.press("Escape"); await expect(panel).toHaveCount(0); await expect(faultedButton).toBeFocused();
  assert.deepEqual(await value(await page.request.get(mapPath)), before);
  cases.push("native-adoption-historical-mismatch-does-not-claim-current-acceptance", "native-adoption-direct-low-authority-page-and-keyboard-details",
    "controlled-adoption-copy-preserves-exact-reported-registration", "native-adoption-observation-does-not-change-map");
}

async function exactRelease(page, id) {
  await page.getByLabel("Exact retained release ID", { exact: true }).fill(id);
  await page.getByRole("button", { name: "Inspect exact release", exact: true }).click();
  await expect(page.getByRole("region", { name: "Selected exact release", exact: true })).toBeVisible();
}
async function review(page) {
  await page.getByRole("button", { name: "Review complete assignment", exact: true }).focus(); await page.keyboard.press("Enter");
  await expect(page.getByRole("heading", { name: "Review assignment map", exact: true })).toBeFocused();
}
async function submit(page, url) {
  const response = page.waitForResponse(item => item.url() === url && item.request().method() === "PUT");
  await page.getByRole("button", { name: "Confirm assignment", exact: true }).focus(); await page.keyboard.press("Enter");
  return value(await response);
}
async function dropCommittedAssignment(page, url) {
  let entered = false, result, failure, settle;
  const joined = new Promise(resolve => { settle = resolve; });
  await page.route(url, async route => {
    if (route.request().method() !== "PUT") { await route.continue(); return; }
    entered = true;
    let response;
    try {
      const key = route.request().headers()["idempotency-key"];
      const stored = await page.evaluate(() => sessionStorage.getItem("cis.management.command-receipts.v1"));
      assert.ok(stored.includes(key), "The exact original assignment reference must exist before dispatch.");
      response = await route.fetch({ timeout: 30000 }); result = await value(response);
      assert.equal(result.disposition, "applied"); assert.equal(result.key, key);
      await route.abort("failed");
    } catch (error) { failure = error; try { await route.abort("failed"); } catch { /* Preserve first failure while retaining actual callback custody. */ } }
    finally { if (response !== undefined) { await response.dispose(); } settle(); }
  });
  try {
    await page.getByRole("button", { name: "Confirm assignment", exact: true }).focus(); await page.keyboard.press("Enter");
    await bounded(joined);
    if (failure !== undefined) { throw failure; }
    await expect(page.getByRole("status").filter({ hasText: "Waiting for the current assignment" })).toHaveCount(0);
  } finally { if (entered) { await joined; } await page.unroute(url); }
  return result;
}
async function value(response) { return (await protectedReply(response)).value; }
async function protectedReply(response) {
  assert.equal(response.status(), 200); const reply = await response.json(); assert.equal(reply.state, "ready");
  const context = capturePortalContext(reply.context); assert.ok(context); return { value: reply.value, context };
}
async function bounded(promise) {
  let timer;
  try { return await Promise.race([promise, new Promise((_, reject) => { timer = setTimeout(() => reject(new Error("The owned native assignment callback must settle.")), 30000); })]); }
  finally { clearTimeout(timer); }
}
