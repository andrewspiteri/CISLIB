// Current editor Refresh through the ordinary production portal and native Configuration authority.
import assert from "node:assert/strict";
import { randomUUID, createHash } from "node:crypto";
import { expect } from "@playwright/test";
import { configurationRoute } from "../src/portal-routes.ts";
import { portalContextHeader, portalContextKey } from "../src/portal-context.ts";

const raw = String.raw`{"Refresh":"Keep this draft · 日本語 🌍 e\u0301","Count":9007199254740993,"Enabled":false}`;
const note = "Saved after current editor context Refresh";
const concurrentRaw = String.raw`{"Concurrent":"A different operator changed this source","Enabled":true}`;

export async function verifyConfigurationEditorRefreshBrowser({ page, origin, applicationA, refreshOperatorContext: other, editorRefresh: seed, screenshot, cases }) {
  const base = `${origin}/api/portal/applications/${applicationA}/configurations/shared.features`;
  const editorUrl = base + "/editor?schemaVersion=1&source=application";
  const mapUrl = base + "/assignments?schemaVersion=1";
  const path = origin + configurationRoute({ applicationId: applicationA, applicationName: "managed-a", configurationId: "shared.features", schemaVersion: "1", releaseId: seed.baselineReleaseId });
  const effects = [];
  let preview;
  const observe = request => {
    if (new URL(request.url()).origin !== origin || request.method() !== "POST") { return; }
    const url = new URL(request.url());
    if (url.pathname === "/api/portal/configuration/candidate-preview") { preview = request.postDataJSON(); }
    const family = url.pathname.endsWith("/publications") ? "management.configuration.source.publish"
      : url.pathname.endsWith("/releases") ? "management.configuration.release.create" : undefined;
    if (family !== undefined) { effects.push({ family, key: request.headers()["idempotency-key"] }); }
  };
  page.on("request", observe);
  try {
    await page.goto(path);
    await expect(page.locator(".configuration-selected-version .configuration-json-readonly")).toBeVisible();
    await interceptEditor(page, editorUrl, "unavailable", async ready => {
      await page.getByRole("button", { name: "Edit as new version", exact: true }).click();
      assert.ok(await ready);
      await expect(page.getByText("One editor read was unavailable. Try Refresh.", { exact: true })).toBeVisible();
      assert.deepEqual(effects, []);
    });
    await page.getByRole("button", { name: "Refresh", exact: true }).click();
    const input = page.locator("#configuration-version-json");
    await expect(input).toBeEditable();
    await input.fill(raw);
    await page.getByLabel("Version note", { exact: true }).fill(note);
    const old = await get(page.request, editorUrl);
    assert.equal(old.value.assignment.defaultReleaseId, seed.baselineReleaseId);

    const otherBefore = await get(other, editorUrl);
    const concurrentKey = randomUUID();
    const publication = await command(other, `${origin}/api/portal/configuration/sources/${old.value.source.sourceId}/publications`, "POST",
      concurrentKey, otherBefore.value.source.eTag, otherBefore.context,
      { commandVersion: 1, payloadJson: concurrentRaw, reason: "Concurrent editor Refresh regression" }, origin);
    const mapBefore = await get(other, mapUrl);
    const mapKey = randomUUID();
    await command(other, mapUrl, "PUT", mapKey, mapBefore.value.eTag, mapBefore.context,
      { commandVersion: 1, intent: "replace", defaultReleaseId: seed.candidateReleaseId, overrides: [], reason: "Concurrent deployment before editor Refresh" }, origin);

    let fresh;
    await interceptEditor(page, editorUrl, "hold", async ready => {
      await page.getByRole("button", { name: "Refresh", exact: true }).click();
      fresh = await ready;
      assert.ok(fresh);
      assert.notEqual(fresh.value.source.eTag, old.value.source.eTag);
      assert.notEqual(fresh.value.assignment.eTag, old.value.assignment.eTag);
      assert.equal(fresh.value.assignment.defaultReleaseId, seed.candidateReleaseId);
      await expect(input).toHaveValue(raw);
      await expect(page.getByLabel("Version note", { exact: true })).toHaveValue(note);
      await expect(page.getByRole("button", { name: "Save version", exact: true })).toBeDisabled();
      assert.deepEqual(effects, []);
    });
    await expect(page.getByRole("button", { name: "Save version", exact: true })).toBeEnabled();
    await expect(input).toHaveValue(raw);
    await expect(page.getByLabel("Version note", { exact: true })).toHaveValue(note);
    await page.getByRole("button", { name: "Save version", exact: true }).click();
    const versions = page.getByRole("region", { name: "Saved versions", exact: true });
    await expect(versions.getByRole("button", { name: note, exact: true })).toBeVisible();
    assert.ok(preview);
    assert.equal(preview.sourceETag, fresh.value.source.eTag);
    assert.equal(preview.assignmentETag, fresh.value.assignment.eTag);
    assert.equal(preview.commonSource.publicationId, seed.firstCommonPublicationId);
    assert.equal(preview.applicationSource.publicationId, seed.applicationPublicationId);
    assert.equal(preview.payloadJson, raw);
    assert.deepEqual(effects.map(row => row.family), ["management.configuration.source.publish", "management.configuration.release.create"]);
    const history = await get(page.request, base + "/releases?schemaVersion=1&pageSize=100");
    const saved = history.value.items.find(row => row.reason === note);
    assert.ok(saved);
    assert.equal(saved.commonSource.publicationId, seed.firstCommonPublicationId);
    const content = await get(page.request, base + `/versions/${saved.releaseId}?schemaVersion=1`);
    assert.equal(content.value.content.publication.payloadJson, raw);
    const afterMap = await get(page.request, mapUrl);
    assert.equal(afterMap.value.defaultReleaseId, seed.candidateReleaseId);
    assert.equal(afterMap.value.eTag, fresh.value.assignment.eTag, "Saving a refreshed draft must not deploy it.");
    await screenshot(page, "configuration-editor-refresh-real-concurrent-source-and-map-native");
    cases.push("native-editor-refresh-retries-one-failed-read", "native-editor-refresh-keeps-draft-and-note-during-real-current-read",
      "native-editor-save-uses-refreshed-preconditions-with-original-selected-shared-reference");
    return { applicationId: applicationA, sourceId: old.value.source.sourceId, bindingId: old.value.context.bindingId,
      concurrentKey, concurrentPublicationId: publication.decision.publicationId, concurrentMapKey: mapKey,
      publicationKey: effects[0].key, releaseKey: effects[1].key,
      publicationId: saved.applicationSource.publicationId, releaseId: saved.releaseId,
      sha256: createHash("sha256").update(raw, "utf8").digest("hex"), transientReadRetried: true, draftRetained: true,
      sourceETag: fresh.value.source.eTag, assignmentETag: fresh.value.assignment.eTag };
  } finally { page.off("request", observe); }
}

async function get(api, url) {
  const response = await api.get(url);
  try { assert.equal(response.status(), 200); const body = await response.json(); assert.equal(body.state, "ready"); return body; }
  finally { await response.dispose(); }
}
async function command(api, url, method, key, etag, context, data, origin) {
  const response = await api.fetch(url, { method, headers: { Origin: origin, "Content-Type": "application/json",
    [portalContextHeader]: portalContextKey(context), "Idempotency-Key": key, "If-Match": etag }, data });
  try { assert.equal(response.status(), 200); const body = await response.json(); assert.equal(body.state, "ready");
    assert.equal(body.value.key, key); assert.equal(body.value.disposition, "applied"); return body.value; }
  finally { await response.dispose(); }
}
async function interceptEditor(page, url, mode, action) {
  let entered = false, failure, settle, release, ready;
  const joined = new Promise(resolve => { settle = resolve; });
  const released = new Promise(resolve => { release = resolve; });
  const observed = new Promise(resolve => { ready = resolve; });
  const handler = async route => {
    if (entered || route.request().method() !== "GET") { try { await route.continue(); } catch (error) { failure ??= error; } return; }
    entered = true;
    let response;
    try {
      response = await route.fetch({ timeout: 15000 });
      const body = await response.json();
      assert.equal(response.status(), 200); assert.equal(body.state, "ready");
      ready(body);
      if (mode === "hold") { await released; await route.fulfill({ response }); }
      else { await route.fulfill({ status: 503, headers: { "Content-Type": "application/json", "Cache-Control": "private, no-store" },
        body: JSON.stringify({ state: "unavailable", context: body.context, message: "One editor read was unavailable. Try Refresh." }) }); }
    } catch (error) { failure = error; ready(undefined); try { await route.abort("failed"); } catch { /* Keep the original callback error. */ } }
    finally { try { if (response !== undefined) { await response.dispose(); } } catch (error) { failure ??= error; } finally { settle(); } }
  };
  await page.route(url, handler);
  let timer;
  const bounded = Promise.race([observed, new Promise(resolve => { timer = setTimeout(() => resolve(undefined), 15000); })]);
  try { await action(bounded); }
  finally { clearTimeout(timer); release(); if (entered) { await joined; } await page.unroute(url, handler); }
  if (failure !== undefined) { throw failure; }
}
