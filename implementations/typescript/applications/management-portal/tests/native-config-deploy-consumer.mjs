// Current version-page deployment journeys borrowing the existing native host and browser owners.
import assert from "node:assert/strict";
import { randomUUID } from "node:crypto";
import { expect } from "@playwright/test";
import { configurationRoute } from "../src/portal-routes.ts";
import { capturePortalContext, portalContextHeader, portalContextKey } from "../src/portal-context.ts";

const configurationId = "shared.settings", schemaVersion = "1";

export async function verifyConfigurationDeployApi({ a, b, origin, input, contextA, contextB, cases, reportPhase }) {
  const seed = input.configurationDeploy, base = binding(input.applicationA), readonlyBase = binding(input.applicationB);
  assert.ok(seed && typeof seed.metadataOnly === "boolean");
  reportPhase("current-deploy-api-maps-and-real-retained-versions");
  const before = await get(a, origin + base + "/assignments?schemaVersion=1");
  assert.equal(before.defaultReleaseId, seed.baselineReleaseId);
  assert.equal(before.overrides.length, seed.metadataOnly ? 0 : 2);
  const selected = await get(a, origin + base + `/versions/${seed.candidateReleaseId}?schemaVersion=1`);
  assert.equal(selected.release.releaseId, seed.candidateReleaseId);
  assert.equal(selected.content.state, seed.metadataOnly ? "redacted" : "present");
  if (seed.metadataOnly) { assert.equal("publication" in selected.content, false); }
  const ownB = await get(b, origin + readonlyBase + `/versions/${seed.readonlyReleaseId}?schemaVersion=1`);
  assert.equal(ownB.release.applicationId, input.applicationB);
  assert.equal(ownB.content.state, "redacted"); assert.equal("publication" in ownB.content, false);
  const readonlyMap = await get(b, origin + readonlyBase + "/assignments?schemaVersion=1");
  assert.equal(readonlyMap.defaultReleaseId, undefined); assert.deepEqual(readonlyMap.overrides, []);
  reportPhase("current-deploy-api-preview-is-read-only");
  const command = { commandVersion: 1, intent: "replace", defaultReleaseId: seed.candidateReleaseId, overrides: [], reason: "Current Deploy eligibility observation" };
  const preview = await reply(await a.post(origin + "/api/portal/configuration/preview?pageSize=2", { headers: {
    Origin: origin, "Content-Type": "application/json", [portalContextHeader]: portalContextKey(contextA)
  }, data: { commandVersion: 1, applicationId: input.applicationA, configurationId, schemaVersion, assignment: command } }));
  assert.equal(preview.value.structurallyValid, true);
  assert.equal(preview.value.redacted, seed.metadataOnly);
  assert.deepEqual(preview.value.currentAssignment, before);
  assert.deepEqual(preview.value.proposedAssignment, command);
  assert.deepEqual(await get(a, origin + base + "/assignments?schemaVersion=1"), before);
  assert.deepEqual(await get(b, origin + readonlyBase + "/assignments?schemaVersion=1"), readonlyMap);
  for (const [client, path] of [[a, readonlyBase], [b, base]]) {
    const denied = await client.get(origin + path + "/assignments?schemaVersion=1");
    try { assert.ok([403, 404].includes(denied.status()), "The new journeys must preserve actual cross-application denial."); }
    finally { await denied.dispose(); }
  }
  assert.ok(capturePortalContext(contextB));
  cases.push("current-deploy-real-retained-metadata-and-cross-application-denial", "current-deploy-native-preview-has-no-assignment-effect");
}

export async function verifyConfigurationDeployBrowser({ page, pageB, origin, applicationA, applicationB, configurationDeploy: seed, screenshot, cases }) {
  assert.ok(seed && typeof seed.metadataOnly === "boolean");
  const base = binding(applicationA), mapUrl = origin + base + "/assignments?schemaVersion=1";
  const before = await get(page.request, mapUrl);
  const baseline = await get(page.request, origin + `/api/portal/configuration/releases/${seed.baselineReleaseId}`);
  const candidate = await get(page.request, origin + `/api/portal/configuration/releases/${seed.candidateReleaseId}`);
  const appliedKeys = [];
  let conflictKey, readonlyDeniedKey;
  const pageUrl = origin + configurationRoute({ applicationId: applicationA, applicationName: "managed-a", configurationId, schemaVersion, releaseId: candidate.releaseId });
  await page.goto(pageUrl);
  const versions = page.getByRole("region", { name: "Saved versions", exact: true });
  const row = versions.locator(`[data-version-id="${candidate.releaseId}"]`);
  await expect(row.getByRole("button", { name: "Deploy", exact: true })).toBeEnabled();
  if (seed.metadataOnly) {
    await expect(page.locator(".configuration-json-readonly")).toHaveCount(0);
    await expect(page.getByRole("button", { name: "Edit as new version", exact: true })).toBeDisabled();
    const previewObserved = page.waitForResponse(response => response.request().method() === "POST"
      && new URL(response.url()).pathname === "/api/portal/configuration/preview", { timeout: 30000 })
      .then(response => response.json()).then(value => ({ value }), error => ({ error }));
    let applied, actualPreview;
    try {
      applied = await deploy(page, origin, mapUrl, candidate, { note: "Deploy without source ContentRead" });
      const observed = await previewObserved;
      if ("error" in observed) { throw observed.error; }
      actualPreview = observed.value;
    } finally { await previewObserved; }
    assert.equal(actualPreview.state, "ready"); assert.equal(actualPreview.value.redacted, true);
    appliedKeys.push(applied.key);
    await expect(row).toContainText("Deployed");
    const readonly = await get(pageB.request, origin + binding(applicationB) + `/versions/${seed.readonlyReleaseId}?schemaVersion=1`);
    await pageB.goto(origin + configurationRoute({ applicationId: applicationB, applicationName: "managed-b", configurationId, schemaVersion, releaseId: seed.readonlyReleaseId }));
    const readonlyRow = pageB.getByRole("region", { name: "Saved versions", exact: true }).locator(`[data-version-id="${seed.readonlyReleaseId}"]`);
    await expect(readonlyRow).toContainText(readonly.release.reason);
    await expect(readonlyRow.getByRole("button", { name: "Deploy", exact: true })).toBeDisabled();
    await expect(pageB.getByRole("dialog")).toHaveCount(0);
    await expect(pageB.locator(".configuration-json-readonly")).toHaveCount(0);
    const currentB = await protectedGet(pageB.request, origin + binding(applicationB) + "/assignments?schemaVersion=1");
    readonlyDeniedKey = randomUUID();
    const denied = await reply(await pageB.request.put(origin + binding(applicationB) + "/assignments?schemaVersion=1", { headers: {
      Origin: origin, "Content-Type": "application/json", [portalContextHeader]: portalContextKey(currentB.context),
      "Idempotency-Key": readonlyDeniedKey, "If-Match": currentB.value.eTag
    }, data: { commandVersion: 1, intent: "replace", defaultReleaseId: seed.readonlyReleaseId, overrides: [], reason: "Readonly deployment must be refused" } }));
    assert.equal(denied.value.disposition, "unresolved"); assert.ok([403, 404].includes(denied.value.transport.status));
    assert.deepEqual(await get(pageB.request, origin + binding(applicationB) + "/assignments?schemaVersion=1"), currentB.value);
    await screenshot(page, "configuration-metadata-only-deploy-native");
    await screenshot(pageB, "configuration-readonly-real-version-native");
    cases.push("current-version-metadata-only-deploy-without-content-disclosure", "current-version-readonly-real-release-cannot-deploy");
  } else {
    await heldMapViewChange(page, origin, mapUrl, pageUrl, before);
    cases.push("current-version-held-real-map-read-cannot-revive-a-retired-view");
    assert.deepEqual(before.overrides, [{ cohortId: "canary", releaseId: candidate.releaseId }, { cohortId: "finance", releaseId: seed.financeReleaseId }]);
    let competingKey;
    const previewUrl = url => new URL(url).origin === origin && new URL(url).pathname === "/api/portal/configuration/preview";
    let previewEntered = false, previewFailure, settlePreview;
    const previewJoined = new Promise(resolve => { settlePreview = resolve; });
    await page.route(previewUrl, async route => {
      if (route.request().method() !== "POST") { await route.continue(); return; }
      previewEntered = true;
      let response;
      try {
        response = await route.fetch({ timeout: 30000 });
        const actual = await response.json(); assert.equal(response.status(), 200); assert.equal(actual.state, "ready");
        assert.deepEqual(actual.value.currentAssignment, before);
        const captured = await protectedGet(page.request, mapUrl);
        assert.deepEqual(captured.value, before);
        competingKey = randomUUID();
        const competing = await reply(await page.request.put(mapUrl, { headers: { Origin: origin, "Content-Type": "application/json",
          [portalContextHeader]: portalContextKey(captured.context), "Idempotency-Key": competingKey, "If-Match": captured.value.eTag },
          data: { commandVersion: 1, intent: "replace", defaultReleaseId: before.defaultReleaseId, overrides: before.overrides, reason: "Actual concurrent map original after preview" } }));
        assert.equal(competing.value.disposition, "applied"); assert.equal(competing.value.key, competingKey);
        await route.fulfill({ response });
      } catch (error) { previewFailure = error; try { await route.abort("failed"); } catch { /* Preserve the first fault and actual callback join. */ } }
      finally {
        try { if (response !== undefined) { await response.dispose(); } }
        catch (error) { previewFailure = combinedFailure(previewFailure, error); }
        finally { settlePreview(); }
      }
    });
    let conflicted;
    try {
      conflicted = await deploy(page, origin, mapUrl, candidate, { note: "Stale reviewed deployment must conflict", expected: "conflict", expectedPins: 2 });
      await bounded(previewJoined);
      if (previewFailure !== undefined) { throw previewFailure; }
    } finally { if (previewEntered) { await previewJoined; } await page.unroute(previewUrl); }
    assert.ok(competingKey); appliedKeys.push(competingKey); conflictKey = conflicted.key;
    const afterRace = await get(page.request, mapUrl);
    assert.equal(afterRace.defaultReleaseId, before.defaultReleaseId); assert.deepEqual(afterRace.overrides, before.overrides);
    assert.equal(BigInt(afterRace.generation), BigInt(before.generation) + 1n);
    const original = page.locator(".configuration-recovery");
    await original.locator(":scope > summary").click();
    await expect(original.getByRole("status")).toContainText("Current state changed");
    await original.getByRole("button", { name: "Dismiss resolved reference", exact: true }).click();
    await expect(original).toHaveCount(0);
    await expect(row.getByRole("button", { name: "Deploy", exact: true })).toBeEnabled();
    const committed = await deploy(page, origin, mapUrl, candidate, { note: "Deploy exact candidate and remove both pins", loss: true, expectedPins: 2 });
    appliedKeys.push(committed.key);
    await page.reload();
    const recovery = page.locator(".configuration-recovery");
    await expect(recovery).toContainText("Deployment original result");
    await recovery.locator(":scope > summary").click();
    await expect(versions.locator(`[data-version-id="${baseline.releaseId}"]`).getByRole("button", { name: "Deploy", exact: true })).toBeDisabled();
    const lookupObserved = page.waitForResponse(response => response.request().method() === "GET" && new URL(response.url()).pathname.endsWith(`/assignment-operations/${committed.key}`), { timeout: 30000 })
      .then(response => response.json()).then(value => ({ value }), error => ({ error }));
    let recovered;
    try {
      await recovery.getByRole("button", { name: "Recover original deployment", exact: true }).click();
      const observed = await lookupObserved;
      if ("error" in observed) { throw observed.error; }
      recovered = observed.value;
    } finally { await lookupObserved; }
    assert.equal(recovered.state, "ready"); assert.equal(recovered.value.key, committed.key); assert.equal(recovered.value.disposition, "applied");
    assert.ok(["found", "replayed"].includes(recovered.value.resultSource));
    await expect(recovery.getByRole("status")).toContainText("Original change committed");
    const deployed = await get(page.request, mapUrl);
    assert.equal(deployed.defaultReleaseId, candidate.releaseId); assert.deepEqual(deployed.overrides, []);
    assert.equal(BigInt(deployed.generation), BigInt(before.generation) + 2n);
    await screenshot(page, "configuration-deploy-original-recovery-native");
    const rollback = await deploy(page, origin, mapUrl, baseline, { note: "Deploy retained baseline after same-key recovery" });
    appliedKeys.push(rollback.key);
    await expect(versions.locator(`[data-version-id="${baseline.releaseId}"]`)).toContainText("Deployed");
    await screenshot(page, "configuration-deploy-pins-conflict-rollback-native");
    cases.push("current-version-deploy-removes-explicit-pins", "current-version-real-preview-map-conflict", "current-version-committed-delivery-loss-reloads-and-recovers-same-key", "current-version-retained-pair-rollback-without-republication");
  }
  const final = await get(page.request, mapUrl);
  assert.equal(final.defaultReleaseId, seed.metadataOnly ? candidate.releaseId : baseline.releaseId);
  assert.deepEqual(final.overrides, []);
  assert.equal(BigInt(final.generation), BigInt(before.generation) + BigInt(appliedKeys.length));
  assert.equal(new Set([...appliedKeys, ...(conflictKey === undefined ? [] : [conflictKey])]).size, appliedKeys.length + (conflictKey === undefined ? 0 : 1));
  return { metadataOnly: seed.metadataOnly, appliedKeys, ...(conflictKey === undefined ? {} : { conflictKey }),
    ...(readonlyDeniedKey === undefined ? {} : { readonlyDeniedKey }), finalGeneration: final.generation, finalReleaseId: final.defaultReleaseId };
}

export { deploy as deployRetainedVersion };

async function deploy(page, origin, mapUrl, version, { note, expected = "applied", loss = false, expectedPins = 0 }) {
  const row = page.getByRole("region", { name: "Saved versions", exact: true }).locator(`[data-version-id="${version.releaseId}"]`);
  await row.getByRole("button", { name: "Deploy", exact: true }).click();
  const dialog = page.getByRole("dialog");
  await expect(dialog.getByRole("heading", { name: "Deploy " + version.reason, exact: true })).toBeVisible();
  await expect(dialog.getByRole("heading", { name: /All instances of this application in/u })).toBeVisible();
  if (expectedPins > 0) { await expect(dialog).toContainText(`${expectedPins} existing target-specific assignments will be removed`); }
  await expect(dialog.getByRole("button", { name: "Review deployment", exact: true })).toHaveCount(0);
  await dialog.getByLabel("Deployment note", { exact: true }).fill(note);
  let entered = false, failure, observation, settle;
  const joined = new Promise(resolve => { settle = resolve; });
  await page.route(mapUrl, async route => {
    if (route.request().method() !== "PUT") { await route.continue(); return; }
    const duplicate = entered; entered = true;
    let response;
    try {
      assert.equal(duplicate, false, "One confirmation admits one original request.");
      const key = route.request().headers()["idempotency-key"];
      const retained = await page.evaluate(() => JSON.parse(sessionStorage.getItem("cis.management.command-receipts.v1") ?? "null"));
      assert.ok(retained?.receipts.some(receipt => receipt.key === key && receipt.family === "management.configuration.assignment.replace"
        && receipt.target.applicationId === version.applicationId && receipt.target.configurationId === configurationId && receipt.target.schemaVersion === schemaVersion), "Persist the exact original target before actual native dispatch.");
      const command = JSON.parse(route.request().postData());
      assert.equal(command.defaultReleaseId, version.releaseId); assert.deepEqual(command.overrides, []);
      response = await route.fetch({ timeout: 30000 });
      assert.equal(response.status(), 200);
      const body = await response.json(); assert.equal(body.state, "ready"); observation = body.value;
      assert.equal(observation.key, key); assert.equal(observation.family, "management.configuration.assignment.replace");
      assert.equal(observation.disposition, expected);
      if (loss) { await route.abort("failed"); } else { await route.fulfill({ response }); }
    } catch (error) { failure = error; try { await route.abort("failed"); } catch { /* Join the real callback and retain the first fault. */ } }
    finally {
      try { if (response !== undefined) { await response.dispose(); } }
      catch (error) { failure = combinedFailure(failure, error); }
      finally { settle(); }
    }
  });
  try {
    await dialog.getByRole("button", { name: "Deploy " + version.reason, exact: true }).click();
    await bounded(joined);
    if (failure !== undefined) { throw failure; }
    assert.ok(entered && observation);
    await expect(dialog).not.toBeVisible();
    if (!loss && expected === "applied") { await expect(row).toContainText("Deployed"); }
    return observation;
  } finally { if (entered) { await joined; } await page.unroute(mapUrl); }
}
async function heldMapViewChange(page, origin, mapUrl, pageUrl, expectedMap) {
  let entered = false, failure, admit, release, settle;
  const started = new Promise(resolve => { admit = resolve; }), held = new Promise(resolve => { release = resolve; }), joined = new Promise(resolve => { settle = resolve; });
  const beforeKeys = await receiptKeys(page);
  await page.route(mapUrl, async route => {
    if (route.request().method() !== "GET" || entered) { await route.continue(); return; }
    entered = true;
    let response;
    try {
      response = await route.fetch({ timeout: 30000 });
      assert.equal(response.status(), 200);
      const body = await response.json(); assert.equal(body.state, "ready"); assert.deepEqual(body.value, expectedMap);
      admit(); await held;
      await route.fulfill({ response });
    } catch (error) { if (route.request().failure()?.errorText !== "net::ERR_ABORTED") { failure = error; } admit(); }
    finally {
      try { if (response !== undefined) { await response.dispose(); } }
      catch (error) { failure = combinedFailure(failure, error); }
      finally { settle(); }
    }
  });
  try {
    await page.getByRole("button", { name: "Refresh", exact: true }).click();
    await bounded(started);
    if (failure !== undefined) { throw failure; }
    await page.goto(origin + "/applications");
    await expect(page.getByRole("heading", { name: "Applications", exact: true })).toBeVisible();
  } finally { release(); if (entered) { await joined; } await page.unroute(mapUrl); }
  if (failure !== undefined) { throw failure; }
  await expect(page.getByRole("region", { name: "Saved versions", exact: true })).toHaveCount(0);
  await expect(page.locator(".configuration-json-readonly")).toHaveCount(0);
  assert.deepEqual(await receiptKeys(page), beforeKeys, "A canceled read and view change cannot add or replay a native original.");
  assert.deepEqual(await get(page.request, mapUrl), expectedMap);
  await page.goto(pageUrl);
  await expect(page.getByRole("region", { name: "Saved versions", exact: true })).toBeVisible();
}
async function receiptKeys(page) {
  return page.evaluate(() => {
    const stored = JSON.parse(sessionStorage.getItem("cis.management.command-receipts.v1") ?? "null");
    return (stored?.receipts ?? []).map(receipt => receipt.key).sort();
  });
}
function binding(applicationId) { return `/api/portal/applications/${applicationId}/configurations/${configurationId}`; }
function combinedFailure(previous, current) { return previous === undefined ? current : new AggregateError([previous, current], "The deployment callback and owned cleanup faulted."); }
async function get(client, url) { return (await protectedGet(client, url)).value; }
async function protectedGet(client, url) { return reply(await client.get(url)); }
async function reply(response) {
  try {
    assert.equal(response.status(), 200);
    const body = await response.json(); assert.equal(body.state, "ready");
    const context = capturePortalContext(body.context); assert.ok(context);
    return { value: body.value, context };
  } finally { await response.dispose(); }
}
async function bounded(operation) {
  let timer;
  try { return await Promise.race([operation, new Promise((_, reject) => { timer = setTimeout(() => reject(new Error("The actual deployment callback did not settle.")), 30000); })]); }
  finally { clearTimeout(timer); }
}
