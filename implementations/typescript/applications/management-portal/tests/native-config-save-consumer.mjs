// Two current Configuration Save journeys within the existing owned native portal fixture.
import assert from "node:assert/strict";
import { createHash, randomUUID } from "node:crypto";
import { expect } from "@playwright/test";
import { applicationRoute, configurationRoute } from "../src/portal-routes.ts";
import { capturePortalContext, portalContextHeader, portalContextKey } from "../src/portal-context.ts";

const newId = "portal.partial.save", newNote = "Save recovered through its three original stages";
const newRaw = String.raw`{
  "Greeting": "Recovered Save · 日本語 🌍 e\u0301",
  "Enabled": false,
  "Count": 9007199254740993
}`;
const conflictId = "shared.features", conflictNote = "Complete the frozen version after a map conflict";
const conflictRaw = String.raw`{"Enabled":false,"Features":{"CountrySearch":true,"Export":true},"Large":9007199254740993,"Unicode":"日本語 🌍 e\u0301"}`;

export async function verifyNewConfigurationSaveRecoveryBrowser({ page, origin, applicationA, screenshot, cases }) {
  const effects = observeEffects(page, origin);
  const creationPath = `${origin}/api/portal/applications/${applicationA}/configuration-creations`;
  const base = `/api/portal/applications/${applicationA}/configurations/${newId}`;
  try {
    const route = new URL(applicationRoute({ applicationId: applicationA, applicationName: "managed-a", tab: "configuration" }), origin);
    route.pathname += "/new";
    await page.goto(route.href);
    await page.getByLabel("Configuration name", { exact: true }).fill(newId);
    await page.getByLabel("Version note", { exact: true }).fill(newNote);
    const rawInput = page.getByLabel("Application JSON", { exact: true });
    const receiptsBefore = await page.evaluate(() => sessionStorage.getItem("cis.management.command-receipts.v1"));
    for (const invalid of ['{"Greeting":', '{"Enabled":true,"Enabled":false}']) {
      await rawInput.fill(invalid);
      await page.getByRole("button", { name: "Save version", exact: true }).click();
      await expect(page.getByText("Provide a name, a version note and a complete JSON object within 256 KiB.", { exact: true })).toBeVisible();
      assert.deepEqual(effects.items, []);
      assert.equal(await page.evaluate(() => sessionStorage.getItem("cis.management.command-receipts.v1")), receiptsBefore);
    }
    const retainedInvalid = await rawInput.inputValue();
    await rawInput.fill('{"Large":"' + "x".repeat(262144) + '"}');
    await expect(rawInput).toHaveValue(retainedInvalid, { timeout: 15000 });
    await expect(page.getByText("The previous JSON is retained. Keep the draft within 256 KiB.", { exact: true })).toBeVisible();
    await page.getByRole("button", { name: "Save version", exact: true }).click();
    assert.deepEqual(effects.items, []);
    assert.equal(await page.evaluate(() => sessionStorage.getItem("cis.management.command-receipts.v1")), receiptsBefore);
    cases.push("native-new-save-malformed-duplicate-and-oversized-local-json-create-no-original-or-effect");
    await rawInput.fill(newRaw);
    const creation = await committedDeliveryLoss(page, creationPath, "management.configuration.create",
      () => page.getByRole("button", { name: "Save version", exact: true }).click());
    assert.equal(effects.items.length, 1, "Unknown setup must not start publication or release.");
    await recoverOnlyOriginal(page, creationPath.replace(/configuration-creations$/u, "configuration-creation-operations/") + creation.key);
    const complete = page.getByRole("button", { name: "Complete incomplete Save", exact: true });
    await expect(complete).toBeEnabled();
    assert.equal(effects.items.length, 1, "Original lookup must not resume Save automatically.");
    const sourceId = creation.decision.sourceId;
    const bindingId = creation.decision.bindingId;
    const publicationPath = `${origin}/api/portal/configuration/sources/${sourceId}/publications`;
    const publication = await committedDeliveryLoss(page, publicationPath, "management.configuration.source.publish", () => complete.click());
    assert.equal(effects.items.length, 2, "Unknown publication must not start a release.");
    await expect(page.locator("#configuration-version-json")).toHaveValue(newRaw);
    const initial = await get(page, origin + base + "/assignments?schemaVersion=1");
    assert.equal(initial.bindingId, bindingId);
    assert.equal(initial.defaultReleaseId, undefined);
    assert.equal(initial.generation, undefined);
    assert.deepEqual(initial.overrides, []);
    await recoverOnlyOriginal(page, `${origin}/api/portal/configuration/sources/${sourceId}/publication-operations/${publication.key}`);
    const continueSave = page.getByRole("button", { name: "Continue incomplete Save", exact: true });
    await expect(continueSave).toBeEnabled();
    assert.equal(effects.items.length, 2);
    const releasePath = origin + base + "/releases?schemaVersion=1";
    const release = await committedDeliveryLoss(page, releasePath, "management.configuration.release.create", () => continueSave.click());
    assert.equal(effects.items.length, 3);
    await expect(page.getByRole("region", { name: "Saved versions", exact: true })).toHaveCount(0);
    await recoverOnlyOriginal(page, origin + base + `/release-operations/${release.key}?schemaVersion=1`);
    await expect(page.getByRole("region", { name: "Saved versions", exact: true }).getByRole("button", { name: newNote, exact: true })).toBeVisible();
    const saved = await get(page, origin + base + `/versions/${release.decision.releaseId}?schemaVersion=1`);
    assert.equal(saved.content.state, "present");
    assert.equal(saved.content.layer, "application");
    assert.equal(saved.content.publication.payloadJson, newRaw);
    assert.equal(saved.release.commonSource.state, "absent");
    assert.equal(saved.release.applicationSource.publicationId, publication.decision.publicationId);
    assert.deepEqual(await get(page, origin + base + "/assignments?schemaVersion=1"), initial);
    assert.deepEqual(effects.items.map(item => item.key), [creation.key, publication.key, release.key]);
    assert.deepEqual(effects.items.map(item => item.family), ["management.configuration.create", "management.configuration.source.publish", "management.configuration.release.create"]);
    await expect(page.getByText("Not deployed", { exact: true })).toBeVisible();
    await receiptIsPayloadFree(page, [creation.key, publication.key, release.key], newRaw, newNote);
    await screenshot(page, "configuration-save-three-original-recoveries-native");
    cases.push("native-new-save-three-actual-commits-with-lost-delivery-and-explicit-same-key-recovery",
      "native-recovered-save-never-repeats-setup-publication-release-or-assigns");
    return { applicationId: applicationA, configurationId: newId, sourceId, bindingId,
      publicationId: publication.decision.publicationId, releaseId: release.decision.releaseId,
      sha256: sha(newRaw), effects: effects.items };
  } finally { effects.stop(); }
}

export async function verifyConfigurationReleaseConflictBrowser({ page, origin, applicationA, screenshot, cases }) {
  const base = `/api/portal/applications/${applicationA}/configurations/${conflictId}`;
  const initial = await get(page, origin + base + "/assignments?schemaVersion=1");
  assert.equal(initial.generation, "1");
  assert.deepEqual(initial.overrides, []);
  const baseline = await get(page, origin + `/api/portal/configuration/releases/${initial.defaultReleaseId}`);
  assert.equal(baseline.commonSource.state, "present");
  assert.equal(baseline.applicationSource.state, "absent");
  const effects = observeEffects(page, origin);
  const releasePath = origin + base + "/releases?schemaVersion=1";
  const mapPath = origin + base + "/assignments?schemaVersion=1";
  let competing, refused, entered = false, callbackFailure, settle;
  const settled = new Promise(resolve => { settle = resolve; });
  const conflictRoute = async route => {
    if (route.request().method() !== "POST") { await route.continue(); return; }
    entered = true;
    let response;
    try {
      const request = route.request(), key = request.headers()["idempotency-key"];
      await receiptIsPayloadFree(page, [key], conflictRaw, conflictNote);
      const current = await reply(page, mapPath);
      assert.equal(current.value.eTag, request.headers()["if-match"]);
      const keyForRace = randomUUID();
      const raceResponse = await page.request.put(mapPath, { headers: { Origin: origin, "Content-Type": "application/json",
        [portalContextHeader]: portalContextKey(current.context), "Idempotency-Key": keyForRace, "If-Match": current.value.eTag },
      data: { commandVersion: 1, intent: "replace", defaultReleaseId: current.value.defaultReleaseId,
        overrides: current.value.overrides, reason: "Actual concurrent map decision before saved-version release" } });
      try {
        const race = await raceResponse.json(); assert.equal(race.state, "ready");
        assert.equal(race.value.disposition, "applied"); assert.equal(race.value.key, keyForRace);
        competing = race.value;
      } finally { await raceResponse.dispose(); }
      response = await route.fetch({ timeout: 30000 });
      const value = await response.json(); assert.equal(value.state, "ready");
      assert.equal(value.value.key, key); assert.equal(value.value.disposition, "conflict");
      assert.equal(value.value.originalCompletion, "committed");
      refused = value.value;
      await route.fulfill({ response });
    } catch (error) {
      callbackFailure = error;
      try { await route.abort("failed"); } catch { /* Preserve actual callback failure while cleanup joins. */ }
    } finally {
      try { if (response !== undefined) { await response.dispose(); } }
      catch (error) { callbackFailure = combine(callbackFailure, error); }
      finally { settle(); }
    }
  };
  try {
    await page.goto(origin + configurationRoute({ applicationId: applicationA, applicationName: "managed-a", configurationId: conflictId, schemaVersion: "1" }));
    await page.getByRole("button", { name: "Edit as new version", exact: true }).click();
    await page.locator("#configuration-version-json").fill(conflictRaw);
    await page.getByLabel("Version note", { exact: true }).fill(conflictNote);
    await page.route(releasePath, conflictRoute);
    try {
      await page.getByRole("button", { name: "Save version", exact: true }).click();
      await bounded(settled, "The actual competing assignment and refused release must settle.");
      if (callbackFailure !== undefined) { throw callbackFailure; }
    } finally { if (entered) { await settled; } await page.unroute(releasePath, conflictRoute); }
    assert.ok(competing && refused);
    const publication = effects.items.find(item => item.family === "management.configuration.source.publish");
    assert.ok(publication);
    const pub = await get(page, `${origin}/api/portal/configuration/sources/${publication.sourceId}/publication-operations/${publication.key}`);
    assert.equal(pub.disposition, "applied");
    const knownMap = await get(page, mapPath);
    assert.equal(knownMap.generation, "2");
    assert.equal(knownMap.defaultReleaseId, initial.defaultReleaseId);
    assert.deepEqual(knownMap.overrides, initial.overrides);
    await expect(page.getByText("Current state changed — review again", { exact: true })).toBeVisible();
    const refusedSection = page.locator("details.configuration-recovery > section").filter({ has: page.getByText(refused.key, { exact: true }) });
    await expect(refusedSection).toHaveCount(1);
    const refreshed = page.waitForResponse(response => response.request().method() === "GET"
      && response.url() === origin + base + "/editor?schemaVersion=1&source=application")
      .then(response => response.json()).then(value => ({ value }), error => ({ error }));
    try {
      await refusedSection.getByRole("button", { name: "Dismiss resolved reference", exact: true }).click();
      const observed = await refreshed;
      if ("error" in observed) { throw observed.error; }
      const editorReply = observed.value; assert.equal(editorReply.state, "ready");
      assert.equal(editorReply.value.assignment.eTag, knownMap.eTag);
    } finally { await refreshed; }
    await expect(page.getByText("Refreshing…", { exact: true })).toHaveCount(0);
    await page.evaluate(() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))));
    let previewBody;
    const capturePreview = request => {
      if (request.method() === "POST" && new URL(request.url()).pathname === "/api/portal/configuration/preview") { previewBody = request.postDataJSON(); }
    };
    page.on("request", capturePreview);
    let continued;
    try {
      continued = page.waitForResponse(response => response.url() === releasePath && response.request().method() === "POST")
        .then(response => response.json()).then(value => ({ value }), error => ({ error }));
      const button = page.getByRole("button", { name: "Continue incomplete Save", exact: true });
      await expect(button).toBeEnabled();
      await button.click();
      const observed = await continued;
      if ("error" in observed) { throw observed.error; }
      const result = observed.value; assert.equal(result.state, "ready");
      assert.equal(result.value.disposition, "applied");
      const release = result.value;
      assert.notEqual(release.key, refused.key);
      assert.ok(previewBody);
      assert.equal(previewBody.commonSource.publicationId, baseline.commonSource.publicationId);
      assert.equal(previewBody.applicationSource.publicationId, pub.decision.publicationId);
      assert.equal("payloadJson" in previewBody, false);
      await expect(page.getByRole("region", { name: "Saved versions", exact: true }).getByRole("button", { name: conflictNote, exact: true })).toBeVisible();
      const saved = await get(page, origin + base + `/versions/${release.decision.releaseId}?schemaVersion=1`);
      assert.equal(saved.content.publication.payloadJson, conflictRaw);
      assert.equal(saved.release.commonSource.publicationId, baseline.commonSource.publicationId);
      assert.equal(saved.release.applicationSource.publicationId, pub.decision.publicationId);
      assert.deepEqual(await get(page, mapPath), knownMap);
      assert.equal(effects.items.filter(item => item.family === "management.configuration.source.publish").length, 1);
      assert.equal(effects.items.filter(item => item.family === "management.configuration.release.create").length, 2);
      assert.equal(effects.items.filter(item => item.family === "management.configuration.assignment.replace").length, 0,
        "The one test-owned API race is distinct from the browser Save; the UI never assigns.");
      await screenshot(page, "configuration-save-closed-release-conflict-continued-native");
      cases.push("native-real-map-conflict-closes-one-release-original-before-explicit-reference-only-reconciliation",
        "native-release-only-continuation-keeps-frozen-shared-pair-and-never-republishes-or-assigns");
      return { applicationId: applicationA, configurationId: conflictId, sourceId: publication.sourceId, bindingId: initial.bindingId,
        commonPublicationId: baseline.commonSource.publicationId, initialReleaseId: initial.defaultReleaseId,
        publicationId: pub.decision.publicationId, releaseId: release.decision.releaseId, sha256: sha(conflictRaw),
        publicationKey: publication.key, conflictKey: refused.key, competingKey: competing.key, releaseKey: release.key };
    } finally { if (continued !== undefined) { await continued; } page.off("request", capturePreview); }
  } finally { effects.stop(); }
}

function observeEffects(page, origin) {
  const items = [];
  const observe = request => {
    const url = new URL(request.url()), key = request.headers()["idempotency-key"];
    if (url.origin !== origin || key === undefined || !["POST", "PUT"].includes(request.method())) { return; }
    const source = /\/configuration\/sources\/([0-9a-f-]+)\/publications$/u.exec(url.pathname);
    const family = source ? "management.configuration.source.publish" : url.pathname.endsWith("/configuration-creations") ? "management.configuration.create"
      : url.pathname.endsWith("/releases") ? "management.configuration.release.create" : url.pathname.endsWith("/assignments") ? "management.configuration.assignment.replace" : undefined;
    if (family !== undefined) { items.push({ key, family, ...(source === null ? {} : { sourceId: source[1] }) }); }
  };
  page.on("request", observe);
  return { items, stop: () => page.off("request", observe) };
}
async function committedDeliveryLoss(page, path, family, action) {
  let entered = false, value, failure, settle;
  const joined = new Promise(resolve => { settle = resolve; });
  const handler = async route => {
    if (route.request().method() !== "POST") { await route.continue(); return; }
    entered = true;
    let response;
    try {
      const key = route.request().headers()["idempotency-key"];
      await receiptIsPayloadFree(page, [key], newRaw, newNote);
      response = await route.fetch({ timeout: 30000 });
      const body = await response.json(); assert.equal(body.state, "ready");
      assert.equal(body.value.family, family); assert.equal(body.value.key, key);
      assert.equal(body.value.disposition, "applied"); assert.equal(body.value.originalCompletion, "committed");
      value = body.value;
      await route.abort("failed");
    } catch (error) { failure = error; try { await route.abort("failed"); } catch { /* Join real response cleanup below. */ } }
    finally {
      try { if (response !== undefined) { await response.dispose(); } }
      catch (error) { failure = combine(failure, error); }
      finally { settle(); }
    }
  };
  await page.route(path, handler);
  try {
    await action();
    await bounded(joined, "The actual Save stage and controlled delivery must settle.");
    if (failure !== undefined) { throw failure; }
    assert.ok(value);
    return value;
  } finally { if (entered) { await joined; } await page.unroute(path, handler); }
}
async function recoverOnlyOriginal(page, path) {
  const button = page.locator("button:not([disabled])").filter({ hasText: /^Recover original Save result$/u });
  const response = page.waitForResponse(value => value.url() === path && value.request().method() === "GET")
    .then(value => value.json()).then(value => ({ value }), error => ({ error }));
  try {
    await expect(button).toHaveCount(1);
    await button.click();
    const observed = await response;
    if ("error" in observed) { throw observed.error; }
    const body = observed.value; assert.equal(body.state, "ready");
    assert.equal(body.value.disposition, "applied"); assert.equal(body.value.resultSource, "found");
  } finally { await response; }
}
async function receiptIsPayloadFree(page, keys, raw, note) {
  const stored = await page.evaluate(() => sessionStorage.getItem("cis.management.command-receipts.v1"));
  assert.ok(stored);
  const envelope = JSON.parse(stored);
  for (const key of keys) { assert.ok(stored.includes(key)); }
  assert.equal(stored.includes(raw), false); assert.equal(stored.includes(note), false);
  assert.equal(stored.includes("payloadJson"), false); assert.equal(stored.includes("eTag"), false);
  assert.deepEqual(Object.keys(envelope).sort(), ["operatorId", "receipts", "serviceAssociation", "version"]);
  assert.equal(envelope.version, 1);
  assert.ok(Array.isArray(envelope.receipts) && envelope.receipts.length <= 16);
  for (const row of envelope.receipts) {
    assert.deepEqual(Object.keys(row).sort(), ["createdAtMs", "family", "key", "target"]);
    const fields = row.family === "management.metadata.replace" || row.family === "management.configuration.create" ? ["applicationId"]
      : row.family === "management.configuration.source.publish" ? ["sourceId"] : ["applicationId", "configurationId", "schemaVersion"];
    assert.deepEqual(Object.keys(row.target).sort(), fields.sort());
  }
}
async function reply(page, path) {
  const response = await page.request.get(path);
  try {
    assert.equal(response.status(), 200);
    const body = await response.json(); assert.equal(body.state, "ready");
    const context = capturePortalContext(body.context); assert.ok(context);
    return { context, value: body.value };
  } finally { await response.dispose(); }
}
async function get(page, path) { return (await reply(page, path)).value; }
async function bounded(promise, message) {
  let timer;
  try { return await Promise.race([promise, new Promise((_, reject) => { timer = setTimeout(() => reject(new Error(message)), 30000); })]); }
  finally { clearTimeout(timer); }
}
function sha(raw) { return createHash("sha256").update(raw, "utf8").digest("hex"); }
function combine(first, next) { return first === undefined ? next : new AggregateError([first, next], "Save callback and response cleanup failed."); }
