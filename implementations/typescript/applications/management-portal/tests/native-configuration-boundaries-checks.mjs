// Additional journeys through the current Configuration UI and its ordinary native host.
import assert from "node:assert/strict";
import { createHash } from "node:crypto";
import { expect } from "@playwright/test";
import { configurationRoute } from "../src/portal-routes.ts";

const receiptKey = "cis.management.command-receipts.v1";
const beforeInstancesId = "portal.before.instances";
const beforeInstancesRaw = String.raw` { "Greeting":"Before any instance", "Enabled":false,"Count":9007199254740993,"Text":"日本語 🌍 e\u0301","Optional":null,"Zero":-0 } `;
const beforeInstancesNote = "Saved before the first instance exists";
const sharedId = "shared.features";
const modes = ["quota", "unverified"];

export async function verifyConfigurationBoundaryBrowser({ page, origin, applicationA, operatorAContext, operatorBContext,
  configurationBoundary, screenshot, cases }) {
  const effects = observeEffects(page, origin);
  try {
    const { scenario, applicationId } = configurationBoundary;
    let proof;
    if (scenario === "Predeployment") {
      proof = await predeployment(page, origin, applicationId, effects, screenshot, cases);
    } else {
      assert.equal(applicationId, applicationA);
      const route = origin + configurationRoute({ applicationId, applicationName: "managed-a", configurationId: sharedId,
        schemaVersion: "1", releaseId: configurationBoundary.candidateReleaseId });
      await page.goto(route);
      await expect(page.locator(".configuration-selected-version .configuration-json-readonly")).toBeVisible();
      if (scenario === "SharedReferenceDiff") {
        proof = await sharedReferenceDiff(page, configurationBoundary, screenshot, cases);
      } else {
        assert.equal(scenario, "SessionStorage");
        proof = await sessionStorage(page, origin, route, operatorAContext, operatorBContext, effects, screenshot, cases);
      }
    }
    return { scenario: configurationBoundary.scenario, applicationId: configurationBoundary.applicationId,
      ...(configurationBoundary.baselineReleaseId === undefined || configurationBoundary.baselineReleaseId === null ? {} : { baselineReleaseId: configurationBoundary.baselineReleaseId }),
      ...(configurationBoundary.candidateReleaseId === undefined || configurationBoundary.candidateReleaseId === null ? {} : { candidateReleaseId: configurationBoundary.candidateReleaseId }),
      ...proof, effects: effects.items };
  } finally { effects.stop(); }
}

async function predeployment(page, origin, applicationId, effects, screenshot, cases) {
  await page.goto(origin + "/applications");
  await page.getByLabel("Application name or identity").fill("preallocated-app");
  await page.getByRole("button", { name: "Apply filters", exact: true }).click();
  await page.getByRole("link", { name: "preallocated-app", exact: true }).click();
  await expect(page.getByRole("heading", { name: "No current instances on this page", exact: true })).toBeVisible();
  await expect(page.getByRole("heading", { name: "No past instances on this page", exact: true })).toBeVisible();
  await page.getByRole("tab", { name: "Configuration", exact: true }).click();
  await page.getByRole("link", { name: "New configuration", exact: true }).click();
  await page.getByLabel("Configuration name", { exact: true }).fill(beforeInstancesId);
  const raw = page.getByLabel("Application JSON", { exact: true });
  await raw.fill(beforeInstancesRaw);
  await page.getByLabel("Version note", { exact: true }).fill(beforeInstancesNote);
  for (const mode of modes) {
    const before = await receipts(page);
    await withStorageFailure(page, mode, async () => {
      await page.getByRole("button", { name: "Save version", exact: true }).click();
      await expect(page.getByText("The Save reference could not be retained. Nothing was submitted.", { exact: true })).toBeVisible();
      await expect(raw).toHaveValue(beforeInstancesRaw);
      await expect(page.getByLabel("Configuration name", { exact: true })).toHaveValue(beforeInstancesId);
      await expect(page.getByLabel("Version note", { exact: true })).toHaveValue(beforeInstancesNote);
      assert.deepEqual(effects.items, []);
      assert.equal(await receipts(page), before);
    });
  }
  await page.getByRole("button", { name: "Save version", exact: true }).click();
  const savedVersions = page.getByRole("region", { name: "Saved versions", exact: true });
  const saved = savedVersions.locator("article").filter({ has: page.getByRole("button", { name: beforeInstancesNote, exact: true }) });
  await expect(saved).toBeVisible();
  await expect(page.getByText("Not deployed", { exact: true })).toBeVisible();
  const releaseId = await saved.getAttribute("data-version-id"); assert.ok(releaseId);
  await saved.getByRole("button", { name: "Deploy", exact: true }).click();
  const dialog = page.getByRole("dialog");
  await expect(dialog).toBeVisible();
  await dialog.getByRole("button", { name: "Deploy " + beforeInstancesNote, exact: true }).click();
  await expect(dialog).not.toBeVisible();
  await expect(page.getByText("Deployment selected. Instance status is reported separately.", { exact: true })).toBeVisible();
  await expect(saved.getByText("Deployed", { exact: true })).toBeVisible();
  assert.deepEqual(effects.items.map(item => item.family), ["management.configuration.create", "management.configuration.source.publish",
    "management.configuration.release.create", "management.configuration.assignment.replace"]);
  assert.ok(effects.items.every(item => typeof item.key === "string"));
  const creation = await get(page, `${origin}/api/portal/applications/${applicationId}/configuration-creation-operations/${effects.items[0].key}`);
  const publication = await get(page, `${origin}/api/portal/configuration/sources/${creation.decision.sourceId}/publication-operations/${effects.items[1].key}`);
  assert.equal(creation.disposition, "applied"); assert.equal(publication.disposition, "applied");
  const snapshot = await get(page, `${origin}/api/portal/applications/${applicationId}/configurations/${beforeInstancesId}/versions/${releaseId}?schemaVersion=1`);
  assert.equal(snapshot.content.publication.payloadJson, beforeInstancesRaw);
  assert.equal(snapshot.release.commonSource.state, "absent");
  assert.equal(snapshot.release.applicationSource.publicationId, publication.decision.publicationId);
  await screenshot(page, "configuration-created-saved-deployed-before-instances-native");
  cases.push("native-zero-registration-application-save-and-deploy-uses-four-native-originals-without-enrollment",
    "native-predeployment-save-quota-and-unverified-storage-retain-draft-with-no-transport");
  return { configurationId: beforeInstancesId, schemaVersion: "1", bindingId: creation.decision.bindingId, sourceId: creation.decision.sourceId,
    publicationId: publication.decision.publicationId, releaseId, sha256: sha(beforeInstancesRaw), blockedCreationModes: modes };
}

async function sharedReferenceDiff(page, seed, screenshot, cases) {
  const candidate = page.locator(`[data-version-id="${seed.candidateReleaseId}"]`);
  const baseline = page.locator(`[data-version-id="${seed.baselineReleaseId}"]`);
  await expect(candidate).toHaveClass(/selected/u);
  await expect(baseline.getByText("Deployed", { exact: true })).toBeVisible();
  const comparison = page.locator("details.configuration-comparison");
  assert.equal(await comparison.evaluate(node => node.open), false);
  await comparison.locator(":scope > summary").click();
  const diff = comparison.locator(".configuration-diff");
  await expect(diff.getByText("These saved versions differ.", { exact: true })).toBeVisible();
  await expect(diff.getByText("Shared bases differ. This compares the displayed JSON layers only; it does not compare or merge their Shared content.", { exact: true })).toBeVisible();
  await expect(diff.locator("tr.diff-added, tr.diff-removed, tr.diff-changed")).toHaveCount(0);
  const rows = await diff.locator("tbody tr").evaluateAll(nodes => nodes.map(node => ({ kind: node.className,
    cells: [...node.querySelectorAll("td code")].map(cell => cell.textContent) })));
  assert.ok(rows.length > 0);
  for (const row of rows) { assert.equal(row.kind, "diff-same"); assert.equal(row.cells[0], row.cells[1]); }
  const displayed = await page.locator(".configuration-selected-version .configuration-json-readonly").textContent();
  assert.equal(displayed, '{\n  "Display": "Shared-reference-only example",\n  "Enabled": false,\n  "Count": 9007199254740993\n}');
  await screenshot(page, "configuration-diff-only-immutable-shared-reference-native");
  cases.push("native-diff-detects-real-shared-reference-change-with-identical-shared-and-application-json");
  return { baselineReleaseId: seed.baselineReleaseId, candidateReleaseId: seed.candidateReleaseId,
    equalDisplayedJson: true, sharedReferenceNotice: true, unchangedRows: rows.length };
}

async function sessionStorage(page, origin, route, operatorAContext, operatorBContext, effects, screenshot, cases) {
  const draft = String.raw`{"Boundary":"Current Save draft · 日本語 🌍 e\u0301","Count":9007199254740993}`;
  const note = "Unretained Save must stay local";
  await beginEdit(page, draft, note);
  for (const mode of modes) {
    const before = await receipts(page);
    await withStorageFailure(page, mode, async () => {
      await observedResponse(page, response => new URL(response.url()).pathname === "/api/portal/configuration/candidate-preview"
        && response.request().method() === "POST", () => page.getByRole("button", { name: "Save version", exact: true }).click());
      await expect(page.getByText("Save did not confirm every stage. Keep its exact references and recover the original result; no automatic replacement was submitted.", { exact: true })).toBeVisible();
      await expect(page.getByRole("button", { name: "Refresh", exact: true })).toBeEnabled();
      await expect(page.locator("#configuration-version-json")).toHaveValue(draft);
      await expect(page.getByLabel("Version note", { exact: true })).toHaveValue(note);
      assert.deepEqual(effects.items, []); assert.equal(await receipts(page), before);
    });
  }
  await discardEditor(page);
  for (const mode of modes) {
    const before = await receipts(page);
    const row = page.locator("article.configuration-version-row.selected");
    await row.getByRole("button", { name: "Deploy", exact: true }).click();
    const dialog = page.getByRole("dialog");
    await expect(dialog).toBeVisible();
    await withStorageFailure(page, mode, async () => {
      await observedResponse(page, response => new URL(response.url()).pathname === "/api/portal/configuration/preview"
        && response.request().method() === "POST", () => dialog.getByRole("button", { name: /^Deploy /u }).click());
      await expect(dialog.getByText("The original deployment reference could not be retained. Nothing was deployed.", { exact: true })).toBeVisible();
      await expect(dialog.getByRole("button", { name: "Cancel", exact: true })).toBeEnabled();
      assert.deepEqual(effects.items, []); assert.equal(await receipts(page), before);
    });
    await dialog.getByRole("button", { name: "Cancel", exact: true }).click();
    await expect(dialog).not.toBeVisible();
  }
  await screenshot(page, "configuration-current-save-deploy-storage-boundaries-native");
  await changedSessionDuringSave(page, origin, operatorBContext, effects);
  // Reuse the untouched real A API identity in a fresh document. No API session is invalidated.
  await replaceCookies(page.context(), operatorAContext);
  await page.goto(route);
  const expiredDraft = '{"Boundary":"Discard this draft on a real unauthenticated refresh"}';
  await beginEdit(page, expiredDraft, "Unauthenticated context must clear this note");
  await page.context().clearCookies();
  const unauthorized = await observedResponse(page, response => response.status() === 401
    && new URL(response.url()).pathname.startsWith("/api/portal/"), () => page.getByRole("button", { name: "Refresh", exact: true }).click(), false);
  assert.equal(unauthorized.status, 401); assert.equal(unauthorized.body.state, "unauthenticated");
  await expect(page.locator("#configuration-version-json")).toHaveCount(0);
  await expect(page.getByText("Sign in required", { exact: true }).first()).toBeVisible();
  assert.equal(await page.locator("body").textContent().then(text => text.includes(expiredDraft)), false);
  assert.deepEqual(effects.items, []);
  await screenshot(page, "configuration-native-session-retirement-no-mutations");
  cases.push("native-current-save-and-deploy-quota-and-unverified-storage-refuse-before-transport",
    "native-real-changed-user-retires-pending-save-and-joins-delayed-original-preview",
    "native-current-editor-real-401-clears-local-json-without-native-mutation");
  return { blockedSaveModes: modes, blockedDeployModes: modes, changedOperatorRetiredDraft: true, delayedPreviewJoined: true, unauthenticatedReadRetiredDraft: true };
}

async function changedSessionDuringSave(page, origin, operatorBContext, effects) {
  const raw = '{"Boundary":"Old operator draft must never reach publication"}';
  await beginEdit(page, raw, "Old operator note must disappear");
  let entered = false, response, callbackFailure, observedContext, release, settle, signalEntered;
  const held = new Promise(resolve => { release = resolve; });
  const joined = new Promise(resolve => { settle = resolve; });
  const started = new Promise(resolve => { signalEntered = resolve; });
  const path = origin + "/api/portal/configuration/candidate-preview?pageSize=20";
  const handler = async route => {
    entered = true;
    try {
      response = await route.fetch({ timeout: 30000 });
      const body = await response.json(); assert.equal(body.state, "ready");
      observedContext = body.context; assert.ok(observedContext);
      signalEntered(); await held;
      try { await route.fulfill({ response }); }
      catch (error) {
        if (route.request().failure()?.errorText !== "net::ERR_ABORTED") { throw error; }
      }
    } catch (error) {
      callbackFailure = error; signalEntered();
      try { await route.abort("failed"); } catch { /* Preserve the original callback failure. */ }
    } finally {
      try { if (response !== undefined) { await response.dispose(); } }
      catch (error) { callbackFailure = combine(callbackFailure, error); }
      finally { settle(); }
    }
  };
  await page.route(path, handler);
  try {
    await page.getByRole("button", { name: "Save version", exact: true }).click();
    await bounded(started, "The real A preview must reach its bounded response boundary.");
    if (callbackFailure !== undefined) { throw callbackFailure; }
    await replaceCookies(page.context(), operatorBContext);
    const changed = await observedResponse(page, reply => new URL(reply.url()).pathname.endsWith("/adoption"),
      () => page.getByRole("button", { name: "Instance status", exact: true }).click(), false);
    assert.equal(changed.status, 409);
    assert.ok(changed.body.context); assert.notDeepEqual(changed.body.context, observedContext);
    release(); await bounded(joined, "The retired A preview callback must be joined.");
    if (callbackFailure !== undefined) { throw callbackFailure; }
    await expect(page.locator("#configuration-version-json")).toHaveCount(0);
    await expect(page.getByRole("button", { name: "Refresh", exact: true })).toBeEnabled();
    const text = await page.locator("body").textContent();
    assert.equal(text.includes(raw), false); assert.equal(text.includes("Old operator note must disappear"), false);
    assert.deepEqual(effects.items, []);
  } finally {
    release();
    try { if (entered) { await bounded(joined, "The held native preview must drain during cleanup."); } }
    finally { await page.unroute(path, handler); }
  }
}

async function beginEdit(page, raw, note) {
  await page.getByRole("button", { name: "Edit as new version", exact: true }).click();
  await page.locator("#configuration-version-json").fill(raw);
  await page.getByLabel("Version note", { exact: true }).fill(note);
  await expect(page.getByRole("button", { name: "Save version", exact: true })).toBeEnabled();
}
async function discardEditor(page) {
  const dialog = page.waitForEvent("dialog").then(async value => { assert.equal(value.message(), "Discard unsaved JSON?"); await value.accept(); })
    .then(() => ({ accepted: true }), error => ({ error }));
  try {
    await page.getByRole("button", { name: "Cancel edits", exact: true }).click();
    const result = await dialog; if ("error" in result) { throw result.error; }
    await expect(page.locator("#configuration-version-json")).toHaveCount(0);
  } finally { await dialog; }
}
async function withStorageFailure(page, mode, action) {
  await page.evaluate(({ mode, key }) => {
    if (globalThis.__cisBoundaryStorage !== undefined) { throw new Error("Storage boundary already owned."); }
    const descriptor = Object.getOwnPropertyDescriptor(Storage.prototype, "setItem");
    const state = { descriptor, attempts: 0 };
    globalThis.__cisBoundaryStorage = state;
    Object.defineProperty(Storage.prototype, "setItem", { ...descriptor, value: function (name, value) {
      if (this === window.sessionStorage && name === key) {
        state.attempts++;
        if (mode === "quota") { throw new DOMException("Owned synthetic receipt quota boundary", "QuotaExceededError"); }
        return;
      }
      return descriptor.value.call(this, name, value);
    } });
  }, { mode, key: receiptKey });
  let attempts;
  try { await action(); }
  finally {
    attempts = await page.evaluate(() => {
      const state = globalThis.__cisBoundaryStorage;
      Object.defineProperty(Storage.prototype, "setItem", state.descriptor);
      delete globalThis.__cisBoundaryStorage;
      return state.attempts;
    });
  }
  assert.ok(attempts > 0, "The actual current caller must attempt to retain its original before failing.");
}
async function observedResponse(page, predicate, action, ready = true) {
  // Attach both branches immediately and always join, including when the click/assertion fails.
  const response = page.waitForResponse(predicate).then(async reply => ({ status: reply.status(), body: await reply.json() }))
    .then(value => ({ value }), error => ({ error }));
  try {
    await action(); const result = await response;
    if ("error" in result) { throw result.error; }
    if (ready) { assert.equal(result.value.status, 200); assert.equal(result.value.body.state, "ready"); }
    return result.value;
  } finally { await response; }
}
function observeEffects(page, origin) {
  const items = [];
  const listener = request => {
    const url = new URL(request.url());
    if (url.origin !== origin || !["POST", "PUT"].includes(request.method())) { return; }
    const source = /\/configuration\/sources\/([0-9a-f-]+)\/publications$/u.exec(url.pathname);
    const family = source ? "management.configuration.source.publish" : url.pathname.endsWith("/configuration-creations") ? "management.configuration.create"
      : url.pathname.endsWith("/releases") ? "management.configuration.release.create" : url.pathname.endsWith("/assignments") ? "management.configuration.assignment.replace" : undefined;
    if (family !== undefined) { items.push({ family, key: request.headers()["idempotency-key"] ?? null, ...(source === null ? {} : { sourceId: source[1] }) }); }
  };
  page.on("request", listener);
  return { items, stop: () => page.off("request", listener) };
}
async function replaceCookies(context, api) {
  const state = await api.storageState();
  await context.clearCookies(); await context.addCookies(state.cookies);
}
async function receipts(page) { return page.evaluate(key => sessionStorage.getItem(key), receiptKey); }
async function get(page, path) {
  const response = await page.request.get(path);
  try { assert.equal(response.status(), 200); const body = await response.json(); assert.equal(body.state, "ready"); return body.value; }
  finally { await response.dispose(); }
}
async function bounded(promise, message) {
  let timer;
  try { return await Promise.race([promise, new Promise((_, reject) => { timer = setTimeout(() => reject(new Error(message)), 30000); })]); }
  finally { clearTimeout(timer); }
}
function sha(raw) { return createHash("sha256").update(raw, "utf8").digest("hex"); }
function combine(first, next) { return first === undefined ? next : new AggregateError([first, next], "Boundary callback and response cleanup failed."); }
