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
  const assignmentPath = origin + `/api/portal/applications/${applicationA}/configurations/shared.settings/assignments?schemaVersion=1`;
  const initialMap = await body(await page.request.get(assignmentPath));
  assert.equal(initialMap.defaultReleaseId, undefined); assert.equal(initialMap.generation, undefined); assert.deepEqual(initialMap.overrides, []);
  await page.goto(origin + `/applications/${applicationA}?tab=configuration`);
  await page.getByRole("button", { name: "shared.settings", exact: true }).click();
  await page.getByRole("link", { name: "Edit Application JSON", exact: true }).focus(); await page.keyboard.press("Enter");
  await expect(page.getByRole("heading", { name: "shared.settings", exact: true })).toBeVisible();
  await expect(page.getByText("No default release is assigned.", { exact: false })).toBeVisible();
  await expect(page.getByRole("heading", { name: "Exact source references", exact: true })).toHaveCount(0);
  await expect(page.getByLabel("Greeting", { exact: true })).toHaveCount(0);
  await expect(page.getByLabel("Count", { exact: true })).toHaveCount(0);
  const raw = page.locator("#configuration-raw-json");
  await expect(raw).toHaveValue("{}");
  const rawBefore = ' { "Unrelated":9007199254740993, "Escaped":"\\u00e9", "Zero":0, "Optional":null, "Enabled":false, "Greeting":"Portal exact 🌍 e\u0301", "Count":0 } ';
  await raw.fill(rawBefore);
  await page.getByRole("button", { name: "Format JSON", exact: true }).click();
  let reviewed = await raw.inputValue();
  const expectedFormatted = '{\n  "Unrelated": 9007199254740993,\n  "Escaped": "\\u00e9",\n  "Zero": 0,\n  "Optional": null,\n  "Enabled": false,\n  "Greeting": "Portal exact 🌍 e\u0301",\n  "Count": 0\n}';
  assert.equal(reviewed, expectedFormatted, "Formatting changes whitespace while preserving exact large integer, escape, null, false, Unicode and member-order tokens.");
  await page.getByLabel("Change reason", { exact: true }).fill("Review exact source bytes");
  await page.getByRole("button", { name: "Validate JSON", exact: true }).click();
  await expect(page.getByText("Declared structure is valid", { exact: true })).toBeVisible();
  await page.getByRole("button", { name: "Review Application JSON version", exact: true }).focus(); await page.keyboard.press("Enter");
  const firstChoice = page.getByRole("region", { name: "First release source choice", exact: true });
  const noShared = firstChoice.getByRole("radio", { name: "Use no shared publication · Application only", exact: true });
  await expect(firstChoice).toBeVisible(); await expect(noShared).not.toBeChecked();
  await expect(firstChoice.getByRole("radio", { name: "Choose a shared publication", exact: true })).not.toBeChecked();
  await expect(firstChoice.getByRole("button", { name: "Continue to JSON version review", exact: true })).toBeDisabled();
  await noShared.check();
  await firstChoice.getByRole("button", { name: "Continue to JSON version review", exact: true }).click();
  await expect(page.getByRole("heading", { name: "Review Application JSON version", exact: true })).toBeFocused();
  await page.getByRole("button", { name: "Format JSON", exact: true }).click();
  await expect(page.getByRole("heading", { name: "Review Application JSON version", exact: true })).toHaveCount(0);
  await expect(page.getByRole("button", { name: "Publish Application JSON version", exact: true })).toHaveCount(0);
  assert.equal(await raw.inputValue(), reviewed, "Even a token-preserving explicit format invalidates the earlier review rather than authorizing a stale confirmation.");
  await page.getByRole("button", { name: "Review Application JSON version", exact: true }).click();
  await expect(page.getByRole("heading", { name: "Review Application JSON version", exact: true })).toBeFocused();
  const exactLarge = reviewed.replace('"Count": 0', '"Count": 9007199254740993');
  assert.notEqual(exactLarge, reviewed);
  await raw.fill(exactLarge);
  await expect(page.getByRole("heading", { name: "Review Application JSON version", exact: true })).toHaveCount(0);
  await expect(page.getByRole("button", { name: "Publish Application JSON version", exact: true })).toHaveCount(0);
  await expect(page.getByRole("button", { name: "Validate JSON", exact: true })).toBeEnabled();
  reviewed = await raw.inputValue();
  assert.equal(reviewed, exactLarge, "The JSON editor retains the exact integer token beyond JavaScript's safe integer range.");
  await page.getByRole("button", { name: "Review Application JSON version", exact: true }).click();
  await expect(page.getByRole("heading", { name: "Review Application JSON version", exact: true })).toBeFocused();
  await screenshot(page, "configuration-candidate-review-native");
  const publicationPath = /\/api\/portal\/configuration\/sources\/[0-9a-f-]+\/publications$/u;
  const publication = await commitAndDrop(page, publicationPath, "Publish Application JSON version", "management.configuration.source.publish");
  const accept = async dialog => { assert.equal(dialog.type(), "beforeunload"); await dialog.accept(); };
  page.once("dialog", accept); try { await page.reload(); } finally { page.off("dialog", accept); }
  await expect(page.locator("#configuration-raw-json")).toHaveValue("{}", { timeout: 15000 });
  const publicationOriginal = page.getByRole("region", { name: "Source publication original", exact: true });
  await publicationOriginal.getByRole("button", { name: "Recover original Configuration result", exact: true }).click();
  await expect(publicationOriginal.getByRole("heading", { name: "Publication committed — not assigned", exact: true })).toBeVisible();
  await expect(publicationOriginal.getByRole("button", { name: "Recover original Configuration result", exact: true })).toBeDisabled();
  const publicationId = publication.decision.publicationId;
  const sourceMatch = /\/sources\/([0-9a-f-]+)\/publications$/u.exec(publication.url);
  const sourceId = sourceMatch[1];
  const contentReply = await page.request.get(origin + `/api/portal/applications/${applicationA}/configurations/shared.settings/publication/${publicationId}?schemaVersion=1&source=application`);
  const content = await body(contentReply);
  assert.equal(content.payloadJson, reviewed); assert.equal(content.sha256, createHash("sha256").update(reviewed, "utf8").digest("hex"));
  await publicationOriginal.getByRole("button", { name: "Review release with this publication", exact: true }).click();
  await page.locator("#configuration-release-reason").fill("Separate coherent release");
  await page.getByRole("button", { name: "Review release", exact: true }).click();
  await expect(noShared).not.toBeChecked(); await noShared.check();
  await firstChoice.getByRole("button", { name: "Continue to release review", exact: true }).click();
  await expect(page.getByRole("heading", { name: "Review release: Shared + Application", exact: true })).toBeFocused();
  await screenshot(page, "configuration-release-review-native");
  const releasePath = new RegExp(`/api/portal/applications/${applicationA}/configurations/shared\\.settings/releases\\?schemaVersion=1$`, "u");
  const release = await commitAndDrop(page, releasePath, "Create release — not assigned", "management.configuration.release.create");
  await expect(publicationOriginal.getByRole("heading", { name: "Publication committed — not assigned", exact: true })).toBeVisible();
  const releaseOriginal = page.getByRole("region", { name: "Coherent release original", exact: true });
  await releaseOriginal.getByRole("button", { name: "Recover original Configuration result", exact: true }).click();
  await expect(releaseOriginal.getByRole("heading", { name: "Release committed — not assigned", exact: true })).toBeVisible();
  await expect(releaseOriginal.getByRole("button", { name: "Recover original Configuration result", exact: true })).toBeDisabled();
  await screenshot(page, "configuration-partial-recovery-native");
  await releaseOriginal.getByRole("button", { name: "Dismiss resolved reference and refresh", exact: true }).click();
  await publicationOriginal.getByRole("button", { name: "Dismiss resolved reference and refresh", exact: true }).click();
  const historyDisclosure = page.locator("summary").filter({ hasText: /^Application publication history$/u });
  await expect(historyDisclosure).toBeVisible(); await historyDisclosure.click();
  const history = page.getByRole("region", { name: "Application publication history", exact: true });
  const ownRow = history.getByRole("row").filter({ hasText: "Review exact source bytes" });
  await ownRow.getByRole("button", { name: "View immutable publication", exact: true }).click();
  await history.getByRole("button", { name: "Use this exact Application publication", exact: true }).click();
  await page.getByLabel("Change reason", { exact: true }).fill("Existing references without another publication");
  await expect(page.getByRole("button", { name: "Review Application JSON version", exact: true })).toHaveCount(0);
  await page.getByRole("button", { name: "Review release", exact: true }).click();
  await expect(noShared).not.toBeChecked(); await noShared.check();
  await firstChoice.getByRole("button", { name: "Continue to release review", exact: true }).click();
  await expect(page.getByRole("heading", { name: "Review release: Shared + Application", exact: true })).toBeVisible();
  const secondResponse = page.waitForResponse(response => response.request().method() === "POST" && releasePath.test(response.url()));
  await page.getByRole("button", { name: "Create release — not assigned", exact: true }).click();
  const second = (await (await secondResponse).json()).value;
  assert.equal(second.disposition, "applied");
  await expect(page.getByRole("heading", { name: "Release committed — not assigned", exact: true })).toBeVisible();
  const map = await body(await page.request.get(assignmentPath));
  assert.deepEqual(map, initialMap, "Every assignment identity, precondition, generation, default and cohort entry must remain exactly unchanged.");
  const sourceHistory = await body(await page.request.get(origin + `/api/portal/applications/${applicationA}/configurations/shared.settings/source-history?schemaVersion=1&source=application`));
  assert.equal(sourceHistory.publications.items.length, 2, "Only the seeded original and one reviewed source publication exist.");
  await screenshot(page, "configuration-existing-reference-release-native");
  const editorPath = new RegExp(`/api/portal/applications/${applicationA}/configurations/shared\\.settings/editor\\?`, "u");
  let foreignReply = false, foreignFailure, foreignEntered = false, settleForeign;
  const foreignJoined = new Promise(resolve => { settleForeign = resolve; });
  await page.route(editorPath, async route => {
    foreignEntered = true;
    let response;
    try {
      response = await route.fetch({ timeout: 30000 });
      const reply = await response.json(); assert.equal(reply.state, "ready"); assert.ok(capturePortalContext(reply.context));
      await route.fulfill({ response, json: { ...reply, value: { ...reply.value, context: { ...reply.value.context, applicationId: applicationB } } } });
      foreignReply = true;
    } catch (error) { foreignFailure = error; try { await route.abort("failed"); } catch { /* Join the actual response before reporting the original failure. */ } }
    finally { if (response !== undefined) { await response.dispose(); } settleForeign(); }
  });
  try {
    await expect(page.locator("#configuration-raw-json")).toBeVisible();
    await page.getByRole("button", { name: "Refresh current state", exact: true }).click();
    await within(foreignJoined, "The controlled foreign editor reply and its owned response must settle.");
    if (foreignFailure !== undefined) { throw foreignFailure; }
    await expect(page.locator("#configuration-raw-json")).toHaveCount(0);
    await expect(page.getByRole("region", { name: "Application publication history", exact: true })).toHaveCount(0);
    await expect(page.getByRole("heading", { name: "Review release: Shared + Application", exact: true })).toHaveCount(0);
    assert.equal(foreignReply, true, "The controlled foreign tuple was delivered with the actual authenticated session marker.");
  } finally { if (foreignEntered) { await foreignJoined; } await page.unroute(editorPath); }
  await pageB.goto(origin + `/applications/${applicationB}/configurations/shared.settings/edit?schemaVersion=1&source=application`);
  await expect(pageB.getByText("Configuration defaults are redacted.", { exact: false })).toBeVisible();
  await expect(pageB.getByRole("button", { name: "Publish Application JSON version", exact: true })).toHaveCount(0);
  await expect(pageB.locator("#configuration-raw-json")).toHaveCount(0);
  cases.push("native-configuration-explicit-baseline-and-exact-lexical-json", "native-configuration-json-edits-and-format-invalidate-confirmation",
    "native-configuration-declared-validation-and-unpublished-preview", "controlled-configuration-foreign-editor-tuple-clears-protected-values",
    "controlled-configuration-publication-delivery-loss-reload-same-key-recovery", "controlled-configuration-partial-release-delivery-loss-same-key-recovery",
    "native-configuration-existing-reference-release-without-publication-or-assignment", "native-configuration-read-only-redacted-editor");
  return { publicationKey: publication.key, publicationId, sourceId, draftSha256: content.sha256,
    releases: [{ key: release.key, releaseId: release.decision.releaseId }, { key: second.key, releaseId: second.decision.releaseId }] };
}

async function body(response) {
  assert.equal(response.status(), 200);
  const reply = await response.json(); assert.equal(reply.state, "ready"); assert.ok(capturePortalContext(reply.context)); return reply.value;
}
async function commitAndDrop(page, pattern, button, family) {
  let entered = false, result, failure, settle;
  const joined = new Promise(resolve => { settle = resolve; });
  await page.route(pattern, async route => {
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
    } catch (error) { failure = error; try { await route.abort("failed"); } catch { /* Preserve the original failure and joined response. */ } }
    finally { if (response !== undefined) { await response.dispose(); } settle(); }
  });
  try {
    await page.getByRole("button", { name: button, exact: true }).focus(); await page.keyboard.press("Enter");
    await within(joined, "The actual Configuration operation and controlled delivery must settle.");
    if (failure !== undefined) { throw failure; }
    await expect(page.getByRole("status").filter({ hasText: "Waiting for the current request" })).toHaveCount(0);
  } finally { if (entered) { await joined; } await page.unroute(pattern); }
  return result;
}
async function within(promise, message) {
  let timer;
  try { return await Promise.race([promise, new Promise((_, reject) => { timer = setTimeout(() => reject(new Error(message)), 12000); })]); }
  finally { clearTimeout(timer); }
}
