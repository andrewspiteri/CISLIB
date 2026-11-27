// Finite assignment continuation borrowing the existing real Management/OIDC/browser owners.
import assert from "node:assert/strict";
import { randomUUID } from "node:crypto";
import { expect } from "@playwright/test";
import { configurationRoute } from "../src/portal-routes.ts";
import { deployRetainedVersion } from "./native-config-deploy-consumer.mjs";
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
  const mapPath = origin + `/api/portal/applications/${applicationA}/configurations/shared.settings/assignments?schemaVersion=1`;
  const mapBefore = await value(await page.request.get(mapPath));
  const baseline = await value(await page.request.get(origin + `/api/portal/configuration/releases/${rollout.baselineReleaseId}`));
  const candidate = await value(await page.request.get(origin + `/api/portal/configuration/releases/${rollout.candidateReleaseId}`));
  const appliedKeys = [];
  await page.goto(origin + configurationRoute({ applicationId: applicationA, applicationName: "managed-a", configurationId: "shared.settings", schemaVersion: "1" }));
  const versions = page.getByRole("region", { name: "Saved versions", exact: true });
  const pager = versions.getByRole("navigation");
  await expect(pager.getByRole("button", { name: "Next", exact: true })).toBeEnabled();
  await pager.getByRole("button", { name: "Next", exact: true }).click();
  await expect(pager.getByRole("button", { name: "Previous", exact: true })).toBeEnabled();
  await pager.getByRole("button", { name: "Previous", exact: true }).click();
  await selectRetainedVersion(page, baseline.releaseId);
  await expect(page.locator(".configuration-json-readonly")).toHaveCount(0);
  await expect(page.getByRole("button", { name: "Edit as new version", exact: true })).toBeDisabled();
  const unified = await deployRetainedVersion(page, origin, mapPath, baseline,
    { note: "Deploy retained baseline to all instances and remove the Finance pin", expectedPins: 1 });
  appliedKeys.push(unified.key);
  const unifiedMap = await value(await page.request.get(mapPath));
  assert.equal(unifiedMap.defaultReleaseId, baseline.releaseId); assert.deepEqual(unifiedMap.overrides, []);
  assert.equal(BigInt(unifiedMap.generation), BigInt(mapBefore.generation) + 1n);
  await selectRetainedVersion(page, candidate.releaseId);
  const promoted = await deployRetainedVersion(page, origin, mapPath, candidate,
    { note: "Deploy exact saved candidate without source ContentRead", loss: true });
  appliedKeys.push(promoted.key);
  await page.reload();
  const original = page.locator(".configuration-recovery");
  await expect(original).toContainText("Deployment original result");
  await original.locator(":scope > summary").click();
  const recoveredResponse = page.waitForResponse(response => response.request().method() === "GET"
    && new URL(response.url()).pathname.endsWith(`/assignment-operations/${promoted.key}`))
    .then(response => value(response)).then(result => ({ result }), error => ({ error }));
  let recovered;
  try {
    await original.getByRole("button", { name: "Recover original deployment", exact: true }).click();
    const observed = await recoveredResponse;
    if ("error" in observed) { throw observed.error; }
    recovered = observed.result;
  } finally { await recoveredResponse; }
  assert.equal(recovered.key, promoted.key); assert.equal(recovered.disposition, "applied");
  assert.ok(["found", "replayed"].includes(recovered.resultSource));
  await expect(original.getByRole("status")).toContainText("Original change committed");
  const promotedMap = await value(await page.request.get(mapPath));
  assert.equal(promotedMap.defaultReleaseId, candidate.releaseId); assert.deepEqual(promotedMap.overrides, []);
  assert.equal(BigInt(promotedMap.generation), BigInt(unifiedMap.generation) + 1n);
  await screenshot(page, "assignment-original-recovery-native");

  await selectRetainedVersion(page, baseline.releaseId);
  const previewUrl = url => new URL(url).origin === origin && new URL(url).pathname === "/api/portal/configuration/preview";
  let entered = false, failure, competingKey, settle;
  const joined = new Promise(resolve => { settle = resolve; });
  await page.route(previewUrl, async route => {
    if (route.request().method() !== "POST") { await route.continue(); return; }
    entered = true;
    let response;
    try {
      response = await route.fetch({ timeout: 30000 });
      const preview = await value(response); assert.deepEqual(preview.currentAssignment, promotedMap); assert.equal(preview.redacted, true);
      const current = await protectedReply(await page.request.get(mapPath));
      competingKey = randomUUID();
      const competing = await value(await page.request.put(mapPath, { headers: { Origin: origin, "Content-Type": "application/json",
        [portalContextHeader]: portalContextKey(current.context), "Idempotency-Key": competingKey, "If-Match": current.value.eTag },
        data: { commandVersion: 1, intent: "replace", defaultReleaseId: current.value.defaultReleaseId,
          overrides: current.value.overrides, reason: "Real concurrent map decision after preview" } }));
      assert.equal(competing.disposition, "applied");
      await route.fulfill({ response });
    } catch (error) { failure = error; try { await route.abort("failed"); } catch { /* Retain the first fault and join the actual callback. */ } }
    finally {
      try { if (response !== undefined) { await response.dispose(); } }
      catch (error) { failure = failure === undefined ? error : new AggregateError([failure, error], "Native preview and cleanup failed."); }
      finally { settle(); }
    }
  });
  let conflict;
  try {
    conflict = await deployRetainedVersion(page, origin, mapPath, baseline,
      { note: "Stale reviewed rollback must conflict", expected: "conflict" });
    await bounded(joined);
    if (failure !== undefined) { throw failure; }
  } finally { if (entered) { await joined; } await page.unroute(previewUrl); }
  assert.ok(competingKey); appliedKeys.push(competingKey);
  if (!(await original.locator(":scope > summary").evaluate(element => element.parentElement.open))) { await original.locator(":scope > summary").click(); }
  await expect(original.getByRole("status")).toContainText("Current state changed");
  await original.getByRole("button", { name: "Dismiss resolved reference", exact: true }).click();
  await expect(original).toHaveCount(0);
  await selectRetainedVersion(page, baseline.releaseId);
  const rollback = await deployRetainedVersion(page, origin, mapPath, baseline, { note: "Deliberate renewed rollback after current map reconciliation" });
  appliedKeys.push(rollback.key);
  const finalMap = await value(await page.request.get(mapPath));
  assert.equal(finalMap.defaultReleaseId, baseline.releaseId); assert.deepEqual(finalMap.overrides, []);
  assert.equal(BigInt(finalMap.generation), BigInt(mapBefore.generation) + 4n);
  assert.equal(new Set([...appliedKeys, conflict.key]).size, 5);
  await screenshot(page, "assignment-retained-release-rollback-native");
  await pageB.goto(origin + configurationRoute({ applicationId: applicationB, applicationName: "managed-b", configurationId: "shared.settings", schemaVersion: "1" }));
  await expect(pageB.getByRole("heading", { name: "shared.settings", exact: true })).toBeVisible();
  await expect(pageB.getByRole("button", { name: "Deploy", exact: true }).and(pageB.locator(":enabled"))).toHaveCount(0);
  await expect(pageB.getByRole("dialog")).toHaveCount(0);
  await expect(pageB.locator(".configuration-json-readonly")).toHaveCount(0);
  cases.push("native-rollout-bounded-saved-version-pagination", "native-rollout-metadata-only-deploy-removes-target-pin",
    "controlled-rollout-native-commit-delivery-loss-same-key-recovery", "native-rollout-real-concurrent-map-conflict-and-deliberate-reconciliation",
    "native-rollout-retained-release-rollback-advances-generation", "native-rollout-read-only-user-has-no-confirm-action");
  return { appliedKeys, conflictKey: conflict.key };
}

async function selectRetainedVersion(page, releaseId) {
  const target = new URL(page.url());
  const applicationId = target.searchParams.get("applicationId");
  assert.ok(applicationId);
  const endpoint = `/api/portal/applications/${applicationId}/configurations/shared.settings/releases`;
  const versions = page.getByRole("region", { name: "Saved versions", exact: true });
  const pager = versions.getByRole("navigation");
  const previous = pager.getByRole("button", { name: "Previous", exact: true });
  if (await previous.isEnabled()) { await turnPage(previous); }
  let retained = await value(await page.request.get(target.origin + endpoint + "?schemaVersion=1&pageSize=10"));
  for (let index = 0; index < 3; index++) {
    if (retained.items.some(item => item.releaseId === releaseId)) {
      const row = versions.locator(`[data-version-id="${releaseId}"]`);
      await expect(row).toBeVisible();
      await row.getByRole("button").first().click();
      await expect(page.getByRole("region", { name: "Selected version", exact: true }).getByRole("heading")).toBeVisible();
      return;
    }
    assert.ok(retained.nextCursor, "The exact retained version must be present in the bounded native catalog.");
    retained = await turnPage(pager.getByRole("button", { name: "Next", exact: true }));
  }
  assert.fail("The exact retained version must be present within the bounded fixture pages.");

  async function turnPage(button) {
    const read = page.waitForResponse(response => response.request().method() === "GET" && new URL(response.url()).pathname === endpoint)
      .then(response => value(response)).then(result => ({ result }), error => ({ error }));
    try {
      await button.click();
      const observed = await read;
      if ("error" in observed) { throw observed.error; }
      return observed.result;
    } finally { await read; }
  }
}

export async function verifyAdoptionBrowser({ page, lookupPage, origin, applicationA, adoptionObservations, screenshot, cases }) {
  const mapPath = origin + `/api/portal/applications/${applicationA}/configurations/shared.settings/assignments?schemaVersion=1`;
  const before = await value(await page.request.get(mapPath));
  await page.getByRole("button", { name: "Instance status", exact: true }).focus(); await page.keyboard.press("Enter");
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
