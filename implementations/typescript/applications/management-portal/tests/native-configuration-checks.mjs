// Configuration continuation of the existing owned Management/Auth.js fixture; no additional host or provider.
import assert from "node:assert/strict";
import { createHash, randomUUID } from "node:crypto";
import { expect } from "@playwright/test";
import { capturePortalContext, portalContextHeader, portalContextKey } from "../src/portal-context.ts";

export async function verifyConfigurationApi({ a, b, lookup, lookupContext, origin, input, contextA, contextB, cases, reportPhase }) {
  const base = `/api/portal/applications/${input.applicationA}/configurations/${input.configurationId}`;
  const query = `schemaVersion=${input.configurationSchema}&source=application`;
  reportPhase("configuration-api-editor-a");
  const editor = await body(await a.get(`${base}/editor?${query}`));
  assert.equal(editor.context.defaults.state, "available");
  assert.equal(editor.content.state, "unselected", "A UUID-ordered history entry cannot become the current source when no release is assigned.");
  assert.equal(editor.source.sourceId, input.configurationSourceId);
  assert.ok(editor.source.publications.items.length > 0, "The real same-actor recovery seed supplies immutable history, not a current assignment.");
  assert.equal(editor.assignment.defaultReleaseId, undefined);
  const headers = { Origin: origin, "Content-Type": "application/json", [portalContextHeader]: portalContextKey(contextA) };
  reportPhase("configuration-api-validate");
  const validation = await body(await a.post("/api/portal/configuration/validate", { headers,
    data: { commandVersion: 1, sourceId: input.configurationSourceId, payloadJson: '{"Enabled":false}' } }));
  assert.equal(validation.valid, true);
  const candidate = { commandVersion: 1, applicationId: input.applicationA, configurationId: input.configurationId, schemaVersion: input.configurationSchema,
    selectedSource: "application", sourceId: input.configurationSourceId, sourceETag: editor.source.eTag, assignmentETag: editor.assignment.eTag,
    commonSource: { state: "absent" }, applicationSource: { state: "absent" }, payloadJson: '{"Enabled":false}' };
  reportPhase("configuration-api-candidate-preview");
  const preview = await body(await a.post("/api/portal/configuration/candidate-preview", { headers, data: candidate }));
  assert.equal(preview.structurallyValid, true); assert.equal(preview.redacted, false);
  reportPhase("configuration-api-editor-repeat");
  const after = await body(await a.get(`${base}/editor?${query}`));
  assert.equal(after.source.publications.items.length, editor.source.publications.items.length);
  assert.equal(after.assignment.eTag, editor.assignment.eTag); assert.equal(after.assignment.defaultReleaseId, undefined);
  reportPhase("configuration-api-editor-read-only");
  const readonly = await body(await b.get(`/api/portal/applications/${input.applicationB}/configurations/${input.configurationId}/editor?${query}`));
  assert.equal(readonly.context.defaults.state, "redacted");
  assert.equal("defaultsJson" in readonly.context.defaults, false);
  reportPhase("configuration-api-read-only-write-refusal");
  const refused = await body(await b.post(`/api/portal/configuration/sources/${readonly.context.applicationSource.sourceId}/publications`, {
    headers: { Origin: origin, "Content-Type": "application/json", [portalContextHeader]: portalContextKey(contextB),
      "Idempotency-Key": randomUUID(), "If-Match": readonly.source.eTag }, data: { commandVersion: 1, payloadJson: "{}", reason: "Must be refused" } }));
  assert.equal(refused.disposition, "unresolved"); assert.ok([403, 404].includes(refused.transport.status));
  reportPhase("configuration-api-raw-grammar");
  for (const raw of ['{"commandVersion":1.0,"sourceId":"' + input.configurationSourceId + '","payloadJson":"{}"}',
    '\ufeff{"commandVersion":1,"sourceId":"' + input.configurationSourceId + '","payloadJson":"{}"}']) {
    assert.equal((await a.post("/api/portal/configuration/validate", { headers, data: raw })).status(), 400);
  }
  const stamp = { [portalContextHeader]: portalContextKey(lookupContext) };
  reportPhase("configuration-api-own-publication-recovery");
  const ownPublication = await body(await lookup.get(`/api/portal/configuration/sources/${input.configurationSourceId}/publication-operations/${input.configurationLookup.publicationKey}`, { headers: stamp }));
  assert.equal(ownPublication.disposition, "applied"); assert.equal(ownPublication.resultSource, "found");
  reportPhase("configuration-api-own-release-recovery");
  const ownRelease = await body(await lookup.get(`${base}/release-operations/${input.configurationLookup.releaseKey}?schemaVersion=${input.configurationSchema}`, { headers: stamp }));
  assert.equal(ownRelease.disposition, "applied"); assert.equal(ownRelease.resultSource, "found");
  reportPhase("configuration-api-removed-editor-refusal");
  const hidden = await lookup.get(`${base}/editor?${query}`); assert.ok([403, 404].includes(hidden.status()));
  reportPhase("configuration-api-removed-write-refusal");
  const removed = await body(await lookup.post(`/api/portal/configuration/sources/${input.configurationSourceId}/publications`, {
    headers: { ...headers, ...stamp, "Idempotency-Key": randomUUID(), "If-Match": editor.source.eTag }, data: { commandVersion: 1, payloadJson: "{}", reason: "Write was removed" } }));
  assert.equal(removed.disposition, "unresolved"); assert.ok([403, 404].includes(removed.transport.status));
  cases.push("native-configuration-unassigned-is-not-latest-history", "native-configuration-validation-preview-no-publication-or-assignment",
    "native-configuration-read-only-default-redaction-and-write-denial", "native-configuration-raw-command-grammar",
    "native-configuration-same-actor-source-release-recovery-with-read-and-write-removed");
  reportPhase("configuration-api-complete");
}

export async function verifyConfigurationBrowser({ page, pageB, origin, applicationA, applicationB, screenshot, cases }) {
  const base = `/api/portal/applications/${applicationA}/configurations/shared.settings`;
  const assignmentPath = origin + base + "/assignments?schemaVersion=1";
  const initialMap = await body(await page.request.get(assignmentPath));
  assert.equal(initialMap.defaultReleaseId, undefined); assert.equal(initialMap.generation, undefined); assert.deepEqual(initialMap.overrides, []);
  await page.goto(origin + `/applications/${applicationA}?tab=configuration`);
  await page.getByRole("button", { name: "shared.settings", exact: true }).click();
  await expect(page.getByRole("heading", { name: "shared.settings", exact: true })).toBeVisible();
  await expect(page.locator(".configuration-deployment-bar")).toContainText("Not deployed");
  await page.getByRole("button", { name: "Edit as new version", exact: true }).focus(); await page.keyboard.press("Enter");
  await expect(page.getByRole("heading", { name: "Exact source references", exact: true })).toHaveCount(0);
  await expect(page.getByLabel("Greeting", { exact: true })).toHaveCount(0);
  await expect(page.getByLabel("Count", { exact: true })).toHaveCount(0);
  const raw = page.locator("#configuration-version-json");
  await expect(raw).toBeEditable();
  await expect(page.getByText("PORTAL_PRIVATE_DEFAULT", { exact: false })).toHaveCount(0);
  const rawBefore = ' { "Unrelated":9007199254740993, "Escaped":"\\u00e9", "Zero":0, "Optional":null, "Enabled":false, "Greeting":"Portal exact 🌍 e\u0301", "Count":0 } ';
  await raw.fill(rawBefore);
  await page.getByRole("button", { name: "Format JSON", exact: true }).click();
  const expectedFormatted = '{\n  "Unrelated": 9007199254740993,\n  "Escaped": "\\u00e9",\n  "Zero": 0,\n  "Optional": null,\n  "Enabled": false,\n  "Greeting": "Portal exact 🌍 e\u0301",\n  "Count": 0\n}';
  assert.equal(await raw.inputValue(), expectedFormatted, "Formatting preserves exact large integers, escapes, null, false, Unicode and member order.");
  const reviewed = expectedFormatted.replace('"Count": 0', '"Count": 9007199254740993');
  await raw.fill(reviewed);
  await page.getByLabel("Version note", { exact: true }).fill("Review exact source bytes");
  await expect(page.getByRole("button", { name: "Review Application JSON version", exact: true })).toHaveCount(0);
  await expect(page.getByRole("button", { name: "Publish Application JSON version", exact: true })).toHaveCount(0);
  await expect(raw).toHaveValue(reviewed);
  const publicationPath = /\/api\/portal\/configuration\/sources\/[0-9a-f-]+\/publications$/u;
  const publication = await commitAndDrop(page, publicationPath, async () => {
    await page.getByRole("button", { name: "Save version", exact: true }).focus(); await page.keyboard.press("Enter");
  }, "management.configuration.source.publish");
  await expect(raw).toHaveValue(reviewed);
  const publicationId = publication.decision.publicationId;
  const sourceId = /\/sources\/([0-9a-f-]+)\/publications$/u.exec(publication.url)[1];
  await recoverSaveOriginal(page, publication.key, origin + `/api/portal/configuration/sources/${sourceId}/publication-operations/${publication.key}`, sourceId, applicationA);
  const continueSave = page.getByRole("button", { name: "Continue incomplete Save", exact: true });
  await expect(continueSave).toBeEnabled();
  const releasePath = new RegExp(`/api/portal/applications/${applicationA}/configurations/shared\\.settings/releases\\?schemaVersion=1$`, "u");
  const release = await commitAndDrop(page, releasePath, () => continueSave.click(), "management.configuration.release.create");
  await recoverSaveOriginal(page, release.key, origin + base + `/release-operations/${release.key}?schemaVersion=1`, sourceId, applicationA);
  await expect(page.getByRole("region", { name: "Saved versions", exact: true }).getByRole("button", { name: "Review exact source bytes", exact: true })).toBeVisible();
  const saved = await body(await page.request.get(origin + base + `/versions/${release.decision.releaseId}?schemaVersion=1`));
  assert.equal(saved.content.state, "present"); assert.equal(saved.content.layer, "application");
  assert.equal(saved.release.commonSource.state, "absent");
  assert.equal(saved.release.applicationSource.publicationId, publicationId);
  assert.equal(saved.content.publication.payloadJson, reviewed);
  const content = saved.content.publication;
  assert.equal(content.sha256, createHash("sha256").update(reviewed, "utf8").digest("hex"));
  assert.deepEqual(await body(await page.request.get(assignmentPath)), initialMap);
  await screenshot(page, "configuration-current-save-partial-recovery-native");

  // A document restart retains only original references, never the prior draft.
  await page.reload();
  await page.getByRole("button", { name: "Edit as new version", exact: true }).click();
  await expect(raw).toHaveValue(reviewed);
  await recoverSaveOriginal(page, publication.key, origin + `/api/portal/configuration/sources/${sourceId}/publication-operations/${publication.key}`, sourceId, applicationA);
  await recoverSaveOriginal(page, release.key, origin + base + `/release-operations/${release.key}?schemaVersion=1`, sourceId, applicationA);
  const stored = await page.evaluate(() => sessionStorage.getItem("cis.management.command-receipts.v1"));
  assert.ok(stored.includes(publication.key) && stored.includes(release.key));
  assert.ok(!stored.includes("Portal exact") && !stored.includes("Review exact source bytes"));

  // The supported release API still accepts exact existing references. The
  // simplified UI deliberately has no separate release-authoring wizard.
  const exact = await page.request.get(assignmentPath);
  let mapReply;
  try { mapReply = await exact.json(); assert.equal(exact.status(), 200); assert.equal(mapReply.state, "ready"); }
  finally { await exact.dispose(); }
  const secondKey = randomUUID();
  const secondResponse = await page.request.post(origin + base + "/releases?schemaVersion=1", { headers: {
    Origin: origin, "Content-Type": "application/json", "Idempotency-Key": secondKey, "If-Match": mapReply.value.eTag,
    [portalContextHeader]: portalContextKey(mapReply.context) }, data: { commandVersion: 1, commonSource: { state: "absent" },
      applicationSource: { state: "present", publicationId }, reason: "Existing references without another publication" } });
  const second = await body(secondResponse);
  assert.equal(second.disposition, "applied"); assert.equal(second.key, secondKey);
  const recoveredSecond = await body(await page.request.get(origin + base + `/release-operations/${secondKey}?schemaVersion=1`, {
    headers: { [portalContextHeader]: portalContextKey(mapReply.context) } }));
  assert.equal(recoveredSecond.key, secondKey); assert.equal(recoveredSecond.resultSource, "found");
  assert.equal(recoveredSecond.decision.releaseId, second.decision.releaseId);
  assert.deepEqual(await body(await page.request.get(assignmentPath)), initialMap);
  const sourceHistory = await body(await page.request.get(origin + base + "/source-history?schemaVersion=1&source=application"));
  assert.equal(sourceHistory.publications.items.length, 2, "Only the seeded original and the one Save publication exist.");
  await screenshot(page, "configuration-existing-reference-release-native");

  const editorPath = new RegExp(`/api/portal/applications/${applicationA}/configurations/shared\\.settings/editor\\?`, "u");
  let foreignReply = false, foreignFailure, foreignEntered = false, settleForeign;
  const foreignJoined = new Promise(resolve => { settleForeign = resolve; });
  const foreignHandler = async route => {
    foreignEntered = true;
    let response;
    try {
      response = await route.fetch({ timeout: 30000 });
      const reply = await response.json(); assert.equal(reply.state, "ready"); assert.ok(capturePortalContext(reply.context));
      await route.fulfill({ response, json: { ...reply, value: { ...reply.value, context: { ...reply.value.context, applicationId: applicationB } } } });
      foreignReply = true;
    } catch (error) { foreignFailure = error; try { await route.abort("failed"); } catch (cleanup) { foreignFailure = combine(foreignFailure, cleanup); } }
    finally { try { if (response !== undefined) { await response.dispose(); } } catch (cleanup) { foreignFailure = combine(foreignFailure, cleanup); } finally { settleForeign(); } }
  };
  await page.route(editorPath, foreignHandler);
  try {
    await expect(raw).toBeVisible();
    await page.getByRole("button", { name: "Refresh", exact: true }).click();
    await within(foreignJoined, "The controlled foreign editor reply and its owned response must settle.");
    if (foreignFailure !== undefined) { throw foreignFailure; }
    await expect(raw).toHaveCount(0);
    await expect(page.getByRole("button", { name: "Save version", exact: true })).toHaveCount(0);
    assert.equal(foreignReply, true, "A foreign exact tuple cannot retain protected values even with the actual session marker.");
  } finally { if (foreignEntered) { await foreignJoined; } await page.unroute(editorPath, foreignHandler); }
  await pageB.goto(origin + `/applications/${applicationB}?tab=configuration`);
  await pageB.getByRole("button", { name: "shared.settings", exact: true }).click();
  const readonlyResponse = pageB.waitForResponse(response => response.request().method() === "GET"
    && response.url() === origin + `/api/portal/applications/${applicationB}/configurations/shared.settings/editor?schemaVersion=1&source=application`, { timeout: 12000 }).then(response => response.json());
  const readonlyOutcomes = await Promise.allSettled([readonlyResponse, pageB.getByRole("button", { name: "Create first version", exact: true }).click()]);
  for (const outcome of readonlyOutcomes) { if (outcome.status === "rejected") { throw outcome.reason; } }
  assert.equal(readonlyOutcomes[0].value.state, "ready");
  assert.equal(readonlyOutcomes[0].value.value.context.defaults.state, "redacted");
  assert.equal("defaultsJson" in readonlyOutcomes[0].value.value.context.defaults, false);
  await pageB.evaluate(() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))));
  await expect(pageB.getByRole("button", { name: "Save version", exact: true })).toHaveCount(0);
  await expect(pageB.locator("#configuration-version-json")).toHaveCount(0);
  await expect(pageB.getByText("PORTAL_PRIVATE_DEFAULT", { exact: false })).toHaveCount(0);
  cases.push("native-configuration-explicit-saved-baseline-and-exact-lexical-json", "native-configuration-current-save-has-no-retired-review-wizard",
    "native-configuration-declared-validation-and-unpublished-preview", "controlled-configuration-foreign-editor-tuple-clears-protected-values",
    "controlled-configuration-publication-and-release-delivery-loss-same-key-recovery", "native-configuration-original-references-survive-document-reload-without-a-draft",
    "native-configuration-existing-reference-release-without-publication-or-assignment", "native-configuration-read-only-redacted-editor");
  return { publicationKey: publication.key, publicationId, sourceId, draftSha256: content.sha256,
    releases: [{ key: release.key, releaseId: release.decision.releaseId }, { key: second.key, releaseId: second.decision.releaseId }] };
}

async function recoverSaveOriginal(page, key, path, sourceId, applicationId) {
  // Unrecovered sections intentionally display no technical key. Correlate the
  // rendered receipt order with its exact, bounded local references instead.
  const selected = await page.evaluate(({ key, sourceId, applicationId }) => {
    const raw = sessionStorage.getItem("cis.management.command-receipts.v1");
    if (raw === null || raw.length > 32768) { throw new Error("The bounded original references are unavailable."); }
    const envelope = JSON.parse(raw);
    if (envelope.version !== 1 || !Array.isArray(envelope.receipts) || envelope.receipts.length > 16) { throw new Error("The original reference envelope is invalid."); }
    const rows = envelope.receipts.filter(receipt => receipt.family === "management.configuration.source.publish"
      ? receipt.target?.sourceId === sourceId : receipt.family === "management.configuration.release.create"
        && receipt.target?.applicationId === applicationId && receipt.target?.configurationId === "shared.settings" && receipt.target?.schemaVersion === "1");
    const indexes = rows.map((receipt, index) => receipt.key === key ? index : -1).filter(index => index >= 0);
    if (indexes.length !== 1) { throw new Error("The exact original reference is not uniquely retained."); }
    return { index: indexes[0], count: rows.length };
  }, { key, sourceId, applicationId });
  const sections = page.getByRole("region", { name: "Edit new version", exact: true }).locator("details.configuration-recovery > section");
  await expect(sections).toHaveCount(selected.count);
  const original = sections.nth(selected.index);
  const response = page.waitForResponse(value => value.url() === path && value.request().method() === "GET", { timeout: 12000 }).then(async value => {
    assert.equal(value.status(), 200); const reply = await value.json(); assert.equal(reply.state, "ready");
    assert.equal(reply.value.key, key); assert.equal(reply.value.disposition, "applied"); assert.equal(reply.value.resultSource, "found");
  });
  const results = await Promise.allSettled([response, original.getByRole("button", { name: "Recover original Save result", exact: true }).click()]);
  let failure;
  for (const result of results) { if (result.status === "rejected") { failure = combine(failure, result.reason); } }
  if (failure !== undefined) { throw failure; }
}
async function body(response) {
  try {
    assert.equal(response.status(), 200);
    const reply = await response.json(); assert.equal(reply.state, "ready"); assert.ok(capturePortalContext(reply.context)); return reply.value;
  } finally { await response.dispose(); }
}
async function commitAndDrop(page, pattern, action, family) {
  let entered = false, result, failure, settle;
  const joined = new Promise(resolve => { settle = resolve; });
  const handler = async route => {
    if (route.request().method() !== "POST") { await route.continue(); return; }
    entered = true;
    let response;
    try {
      const key = route.request().headers()["idempotency-key"];
      const stored = await page.evaluate(() => sessionStorage.getItem("cis.management.command-receipts.v1"));
      assert.ok(stored.includes(key), "The original reference is retained before the actual native operation.");
      response = await route.fetch({ timeout: 30000 });
      const value = await body(response); assert.equal(value.disposition, "applied"); assert.equal(value.family, family); assert.equal(value.key, key);
      result = { ...value, url: route.request().url() };
      await route.abort("failed");
    } catch (error) { failure = combine(failure, error); try { await route.abort("failed"); } catch (cleanup) { failure = combine(failure, cleanup); } }
    finally {
      try { if (response !== undefined) { await response.dispose(); } }
      catch (cleanup) { failure = combine(failure, cleanup); }
      finally { settle(); }
    }
  };
  await page.route(pattern, handler);
  let original;
  try {
    await action();
    await within(joined, "The actual Configuration operation and controlled delivery must settle.");
    if (failure !== undefined) { throw failure; }
    await expect(page.getByRole("status").filter({ hasText: "Waiting for the current request" })).toHaveCount(0);
  } catch (error) { original = error; }
  finally {
    try { if (entered) { await joined; } }
    catch (cleanup) { original = combine(original, cleanup); }
    finally { try { await page.unroute(pattern, handler); } catch (cleanup) { original = combine(original, cleanup); } }
  }
  if (failure !== undefined && failure !== original) { original = combine(original, failure); }
  if (original !== undefined) { throw original; }
  return result;
}
async function within(promise, message) {
  let timer;
  try { return await Promise.race([promise, new Promise((_, reject) => { timer = setTimeout(() => reject(new Error(message)), 12000); })]); }
  finally { clearTimeout(timer); }
}

function combine(first, next) { return first === undefined ? next : first === next ? first : new AggregateError([first, next], 'The Configuration assertion and owned cleanup both failed.'); }
