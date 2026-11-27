// Focused continuation of the existing real Management/Auth.js/PostgreSQL browser fixture.
import assert from "node:assert/strict";
import { createHash } from "node:crypto";
import { expect } from "@playwright/test";
import { applicationRoute, configurationRoute } from "../src/portal-routes.ts";

const firstRaw = String.raw`{"Enabled":false,"Features":{"CountrySearch":true,"Export":false},"Large":9007199254740993,"Unicode":"日本語 🌍 e\u0301"}`;
const secondRaw = String.raw`{"Enabled":false,"Features":{"CountrySearch":true,"Export":true},"Large":9007199254740993,"Unicode":"日本語 🌍 e\u0301"}`;
const newRaw = String.raw`{"Greeting":"Version page","Large":9007199254740993,"Enabled":false}`;
const firstNote = "R09 first saved version", secondNote = "R09 second saved version", newNote = "R09 new Configuration";

export async function verifyFirstConfigurationVersionBrowser({ page, origin, applicationA, screenshot, cases }) {
  const configurationId = "shared.settings", schemaVersion = "1", note = "First version for an existing shared binding";
  const raw = String.raw`{
  "Greeting": "First settings · 日本語 🌍 e\u0301",
  "Enabled": false,
  "Count": 9007199254740993
}`;
  const base = `/api/portal/applications/${applicationA}/configurations/${configurationId}`;
  const initial = await get(page, origin + base + "/assignments?schemaVersion=1");
  assert.equal(initial.defaultReleaseId, undefined);
  assert.equal(initial.generation, undefined);
  assert.deepEqual(initial.overrides, []);
  const history = await get(page, origin + base + "/releases?schemaVersion=1&pageSize=100");
  assert.deepEqual(history.items, []);
  assert.equal(history.nextCursor, undefined);
  const editor = await get(page, origin + base + "/editor?schemaVersion=1&source=application");
  assert.equal(editor.context.commonSource.state, "available", "The existing Common stream is provisioned even though no version was saved.");
  assert.equal(editor.context.applicationSource.state, "available");
  assert.equal(editor.context.defaults.state, "available");
  assert.equal(editor.context.bindingId, initial.bindingId);
  const effects = [];
  const observe = request => {
    const url = new URL(request.url()), key = request.headers()["idempotency-key"];
    if (url.origin !== origin || key === undefined || !["POST", "PUT"].includes(request.method())) { return; }
    const family = url.pathname.endsWith("/publications") ? "management.configuration.source.publish"
      : url.pathname.endsWith("/releases") ? "management.configuration.release.create"
        : url.pathname.endsWith("/assignments") ? "management.configuration.assignment.replace"
          : url.pathname.endsWith("/configuration-creations") ? "management.configuration.create" : undefined;
    if (family !== undefined) { effects.push({ key, family }); }
  };
  page.on("request", observe);
  try {
    await page.goto(origin + configurationRoute({ applicationId: applicationA, applicationName: "managed-a", configurationId, schemaVersion }));
    await expect(page.getByRole("heading", { name: configurationId, exact: true })).toBeVisible();
    await page.getByRole("button", { name: "Create first version", exact: true }).click();
    const input = page.locator("#configuration-version-json");
    await expect(input).toBeEditable();
    await expect(input).toHaveValue("{}\n");
    await input.fill(raw);
    await page.getByLabel("Version note", { exact: true }).fill(note);
    await page.getByRole("button", { name: "Save version", exact: true }).click();
    const versions = page.getByRole("region", { name: "Saved versions", exact: true });
    await expect(versions.getByRole("button", { name: note, exact: true })).toBeVisible();
    const saved = await savedVersion(page, origin, base, note);
    assert.equal(saved.commonSource.state, "absent", "The explicit first Application-only version must not invent a Shared publication.");
    assert.equal(saved.applicationSource.state, "present");
    const detail = await get(page, origin + `${base}/versions/${saved.releaseId}?schemaVersion=1`);
    assert.equal(detail.content.state, "present");
    assert.equal(detail.content.layer, "application");
    assert.equal(detail.content.publication.sourceId, editor.context.applicationSource.sourceId);
    assert.equal(detail.content.publication.payloadJson, raw);
    assert.deepEqual(await get(page, origin + base + "/assignments?schemaVersion=1"), initial);
    assert.deepEqual(effects.map(effect => effect.family), ["management.configuration.source.publish", "management.configuration.release.create"]);
    assert.equal(new Set(effects.map(effect => effect.key)).size, 2);
    await expect(page.getByText("Not deployed", { exact: true })).toBeVisible();
    await screenshot(page, "configuration-first-version-existing-common-stream-native");
    cases.push("native-create-first-version-admits-empty-draft-with-an-existing-common-stream", "native-first-version-saves-exact-formatted-json-with-two-originals-and-no-deployment");
    return { applicationId: applicationA, bindingId: initial.bindingId, sourceId: detail.content.publication.sourceId,
      commonSourceId: editor.context.commonSource.sourceId, publicationId: saved.applicationSource.publicationId, releaseId: saved.releaseId,
      sha256: sha(raw), mapETag: initial.eTag, effects };
  } finally { page.off("request", observe); }
}

export async function verifyConfigurationVersionsBrowser({ page, pageB, origin, applicationA, applicationB, screenshot, cases }) {
  const configurationId = "shared.features", schemaVersion = "1";
  const base = `/api/portal/applications/${applicationA}/configurations/${configurationId}`;
  const initial = await get(page, origin + base + "/assignments?schemaVersion=1");
  assert.equal(initial.generation, "1"); assert.deepEqual(initial.overrides, []);
  const original = await get(page, origin + `/api/portal/configuration/releases/${initial.defaultReleaseId}`);
  assert.equal(original.commonSource.state, "present"); assert.equal(original.applicationSource.state, "absent");
  const originalSnapshot = await get(page, origin + `${base}/versions/${initial.defaultReleaseId}?schemaVersion=1`);
  assert.equal(originalSnapshot.content.state, "present"); assert.equal(originalSnapshot.content.layer, "common");
  const effects = [];
  const observe = request => {
    const url = new URL(request.url()), key = request.headers()["idempotency-key"];
    if (url.origin !== origin || key === undefined || !["POST", "PUT"].includes(request.method())) { return; }
    const family = url.pathname.endsWith("/publications") ? "management.configuration.source.publish"
      : url.pathname.endsWith("/releases") ? "management.configuration.release.create"
        : url.pathname.endsWith("/assignments") ? "management.configuration.assignment.replace"
          : url.pathname.endsWith("/configuration-creations") ? "management.configuration.create" : undefined;
    if (family !== undefined) { effects.push({ key, family }); }
  };
  page.on("request", observe);
  try {
    const route = configurationRoute({ applicationId: applicationA, applicationName: "managed-a", configurationId, schemaVersion });
    await page.goto(origin + route);
    await expect(page.getByRole("heading", { name: configurationId, exact: true })).toBeVisible();
    const versions = page.getByRole("region", { name: "Saved versions", exact: true });
    const originalRow = versions.locator(`[data-version-id="${original.releaseId}"]`);
    await expect(originalRow).toContainText("Deployed");
    const savedDisplay = page.locator(".configuration-selected-version .configuration-json-readonly");
    await expect(savedDisplay).toBeVisible();
    const displayedJson = await savedDisplay.textContent();
    assert.ok(displayedJson.includes("\n") && displayedJson.includes("  \""));
    assert.equal(compactJson(displayedJson), compactJson(originalSnapshot.content.publication.payloadJson));
    const windowGeometry = await nativeWindowBounds(page, screenshot, originalSnapshot.content.publication.payloadJson);
    cases.push("native-owned-window-width-and-height-drive-layout-without-viewport-emulation");
    await desktopBounds(page, screenshot);

    await page.getByRole("button", { name: "Edit as new version", exact: true }).click();
    await expect(page.locator("#configuration-version-json")).toBeEditable();
    await formattedEditorSeed(page, originalSnapshot.content.publication.payloadJson);
    const longRaw = firstRaw.slice(0, -1) + ',"Long":"' + "x".repeat(4096) + '"}';
    await page.locator("#configuration-version-json").fill(longRaw);
    for (const width of [900, 1280, 1920]) {
      await page.setViewportSize({ width, height: 1000 });
      const bounds = await page.locator("#configuration-version-json").evaluate(element => {
        const rect = element.getBoundingClientRect();
        return { right: rect.right, left: rect.left, viewport: document.documentElement.clientWidth, documentWidth: document.documentElement.scrollWidth };
      });
      assert.ok(bounds.documentWidth <= bounds.viewport + 1 && bounds.left >= 0 && bounds.right <= bounds.viewport + 1);
      await expect(page.locator("#configuration-version-json")).toHaveValue(longRaw, { timeout: 5000 });
      await screenshot(page, "r09-version-editor-" + width);
    }
    await page.setViewportSize({ width: 1600, height: 1000 });
    await page.locator("#configuration-version-json").fill(firstRaw);
    await page.getByLabel("Version note", { exact: true }).fill(firstNote);
    await page.getByRole("button", { name: "Save version", exact: true }).click();
    await expect(versions.getByRole("button", { name: firstNote, exact: true })).toBeVisible();
    const first = await savedVersion(page, origin, base, firstNote);
    assert.equal(first.commonSource.publicationId, original.commonSource.publicationId);
    assert.equal(first.applicationSource.state, "present");
    assert.deepEqual(await get(page, origin + base + "/assignments?schemaVersion=1"), initial);
    assert.equal(effects.length, 2, "One Save is exactly a publication and a coherent saved version, with no deployment.");
    cases.push("native-one-save-creates-immutable-pair-without-deploying-or-extra-review-screen");

    await page.getByRole("button", { name: "Edit as new version", exact: true }).click();
    await formattedEditorSeed(page, firstRaw);
    await page.locator("#configuration-version-json").fill(secondRaw);
    await page.getByLabel("Version note", { exact: true }).fill(secondNote);
    await page.getByRole("button", { name: "Save version", exact: true }).click();
    await expect(versions.getByRole("button", { name: secondNote, exact: true })).toBeVisible();
    const second = await savedVersion(page, origin, base, secondNote);
    assert.equal(second.commonSource.publicationId, original.commonSource.publicationId);
    assert.deepEqual(await get(page, origin + base + "/assignments?schemaVersion=1"), initial);
    assert.equal(effects.length, 4);
    const beforeCompare = effects.length;
    await page.getByText("Differences from deployed version", { exact: true }).click();
    await expect(page.getByRole("columnheader", { name: "Deployed version", exact: true })).toBeVisible();
    await expect(page.getByRole("columnheader", { name: "Selected saved version", exact: true })).toBeVisible();
    await expect(page.locator(".configuration-diff")).toContainText("9007199254740993");
    await expect(page.locator(".configuration-diff")).toContainText("false");
    await expect(page.locator(".configuration-diff")).toContainText("true");
    const left = (await page.locator(".configuration-diff tbody tr td:first-child code").allTextContents()).join("\n");
    const right = (await page.locator(".configuration-diff tbody tr td:last-child code").allTextContents()).join("\n");
    assert.equal(compactJson(left), compactJson(originalSnapshot.content.publication.payloadJson), "The left column must be the actual deployed Shared-only version, not another saved Application version.");
    assert.equal(compactJson(right), compactJson(secondRaw), "The right column must retain the selected saved version's exact tokens.");
    assert.equal(effects.length, beforeCompare, "Comparison performs no native mutation.");
    cases.push("native-diff-compares-actual-deployed-version-and-preserves-large-number-tokens");
    await screenshot(page, "r09-saved-version-deployed-diff");

    await deploy(page, versions, second, "Deploy second saved version");
    let current = await get(page, origin + base + "/assignments?schemaVersion=1");
    assert.equal(current.defaultReleaseId, second.releaseId); assert.equal(current.generation, "2"); assert.deepEqual(current.overrides, []);
    await expect(versions.locator(`[data-version-id="${second.releaseId}"]`)).toContainText("Deployed");
    await expect(versions.locator(`[data-version-id="${first.releaseId}"]`)).not.toContainText("Deployed");
    await expect(page.locator(".configuration-diff")).toContainText("No changes in the compared saved layers.");
    cases.push("native-one-confirmation-deploys-exact-selected-version-and-refreshes-current-map");

    await versions.getByRole("button", { name: firstNote, exact: true }).click();
    await expect(page.getByRole("region", { name: "Selected version", exact: true })).toContainText(firstNote);
    await deploy(page, versions, first, "Restore the first saved version");
    current = await get(page, origin + base + "/assignments?schemaVersion=1");
    assert.equal(current.defaultReleaseId, first.releaseId); assert.equal(current.generation, "3"); assert.deepEqual(current.overrides, []);
    await expect(versions.locator(`[data-version-id="${first.releaseId}"]`)).toContainText("Deployed");
    assert.equal(effects.length, 6, "Rollback adds only one new assignment original; it never republishes an old version.");
    const unchangedFirst = await get(page, origin + `${base}/versions/${first.releaseId}?schemaVersion=1`);
    assert.equal(unchangedFirst.content.publication.payloadJson, firstRaw);
    cases.push("native-rollback-reuses-exact-old-pair-with-increasing-generation-and-no-republication");
    await screenshot(page, "r09-older-version-deployed");

    // The independent B account cannot use a foreign application's exact version route.
    const denied = await pageB.request.get(origin + `${base}/versions/${first.releaseId}?schemaVersion=1`);
    try { assert.ok([403, 404].includes(denied.status())); const body = await denied.json(); assert.equal("value" in body, false); }
    finally { await denied.dispose(); }
    cases.push("native-other-operator-cannot-inspect-foreign-version-json");

    const newRoute = new URL(applicationRoute({ applicationId: applicationA, applicationName: "managed-a", tab: "configuration" }), origin);
    newRoute.pathname += "/new";
    await page.goto(newRoute.href);
    await page.getByLabel("Configuration name", { exact: true }).fill("portal.r09.versions");
    await page.getByLabel("Application JSON", { exact: true }).fill(newRaw);
    await page.getByLabel("Version note", { exact: true }).fill(newNote);
    await page.getByRole("button", { name: "Save version", exact: true }).click();
    await expect(page.getByRole("region", { name: "Saved versions", exact: true }).getByRole("button", { name: newNote, exact: true })).toBeVisible();
    const newBase = `/api/portal/applications/${applicationA}/configurations/portal.r09.versions`;
    const created = await savedVersion(page, origin, newBase, newNote);
    assert.equal(created.commonSource.state, "absent");
    const unassigned = await get(page, origin + newBase + "/assignments?schemaVersion=1");
    assert.equal(unassigned.defaultReleaseId, undefined); assert.equal(unassigned.generation, undefined); assert.deepEqual(unassigned.overrides, []);
    const detail = await get(page, origin + `${newBase}/versions/${created.releaseId}?schemaVersion=1`);
    assert.equal(detail.content.publication.payloadJson, newRaw);
    assert.equal(effects.length, 9, "One new Configuration Save has exactly setup, publication and release originals.");
    const dialogs = [];
    const dialogWork = [], dialogErrors = [];
    const accept = dialog => { dialogs.push(dialog.type()); dialogWork.push(dialog.accept().catch(error => dialogErrors.push(error))); };
    page.on("dialog", accept);
    try {
      await page.getByRole("button", { name: "Configuration", exact: true }).click();
      await expect(page).toHaveURL(url => url.pathname.endsWith("/configuration") && url.searchParams.get("applicationId") === applicationA);
      assert.equal(dialogs.length, 0, "A successfully saved new version has no abandoned parent dirty owner.");
    } finally { page.off("dialog", accept); await Promise.all(dialogWork); }
    assert.deepEqual(dialogErrors, []);
    await page.goto(origin + configurationRoute({ applicationId: applicationA, applicationName: "managed-a", configurationId: "portal.r09.versions", schemaVersion, releaseId: created.releaseId }));
    await page.getByRole("button", { name: "Edit as new version", exact: true }).click();
    await page.locator("#configuration-version-json").fill(newRaw + " ");
    const dirtyPrompts = [], dirtyWork = [], dirtyErrors = [];
    let cancelLeave = true;
    const discard = dialog => {
      dirtyPrompts.push(dialog.type());
      dirtyWork.push((cancelLeave ? dialog.dismiss() : dialog.accept()).catch(error => dirtyErrors.push(error)));
    };
    page.on("dialog", discard);
    try {
      await page.getByRole("button", { name: "Configuration", exact: true }).click();
      await expect(page.locator("#configuration-version-json")).toHaveValue(newRaw + " ");
      assert.deepEqual(dirtyPrompts, ["confirm"]);
      cancelLeave = false;
      await page.getByRole("button", { name: "Configuration", exact: true }).click();
      await expect(page).toHaveURL(url => url.pathname.endsWith("/configuration") && url.searchParams.get("applicationId") === applicationA);
      assert.deepEqual(dirtyPrompts, ["confirm", "confirm"], "Each deliberate leave gets one decision and no second unload prompt.");
    } finally { page.off("dialog", discard); await Promise.all(dirtyWork); }
    assert.deepEqual(dirtyErrors, []);
    assert.equal(effects.length, 9, "Discarding local text and following a breadcrumb cannot create any extra native effect.");
    cases.push("native-new-configuration-one-save-handoff-has-no-continue-wizard-or-duplicate-dirty-owner");
    return { applicationId: applicationA, sourceId: unchangedFirst.content.publication.sourceId, bindingId: current.bindingId,
      commonPublicationId: original.commonSource.publicationId, firstPublicationId: first.applicationSource.publicationId,
      secondPublicationId: second.applicationSource.publicationId, firstReleaseId: first.releaseId, secondReleaseId: second.releaseId,
      newSourceId: detail.content.publication.sourceId, newBindingId: unassigned.bindingId,
      newPublicationId: created.applicationSource.publicationId, newReleaseId: created.releaseId,
      firstSha256: sha(firstRaw), secondSha256: sha(secondRaw), newSha256: sha(newRaw), finalGeneration: current.generation, windowGeometry,
      saveLeftDeploymentUnchanged: true, comparedActualDeployed: true, newConfigurationNotDeployed: true, effects };
  } finally { page.off("request", observe); }
}

async function savedVersion(page, origin, base, note) {
  const history = await get(page, origin + base + "/releases?schemaVersion=1&pageSize=100");
  const matches = history.items.filter(item => item.reason === note);
  assert.equal(matches.length, 1); return matches[0];
}
async function deploy(page, versions, version, reason) {
  const row = versions.locator(`[data-version-id="${version.releaseId}"]`);
  await row.getByRole("button", { name: "Deploy", exact: true }).click();
  const dialog = page.getByRole("dialog");
  await expect(dialog.getByRole("heading", { name: "Deploy " + version.reason, exact: true })).toBeVisible();
  await dialog.getByLabel("Deployment note", { exact: true }).fill(reason);
  await expect(dialog.getByRole("button", { name: "Review deployment", exact: true })).toHaveCount(0);
  await dialog.getByRole("button", { name: "Deploy " + version.reason, exact: true }).click();
  await expect(dialog).not.toBeVisible();
  await expect(row).toContainText("Deployed");
}
async function desktopBounds(page, screenshot) {
  for (const width of [900, 1280, 1920]) {
    await page.setViewportSize({ width, height: 1000 });
    await expect(page.getByRole("region", { name: "Saved versions", exact: true })).toBeVisible();
    const bounds = await page.evaluate(() => ({ width: document.documentElement.clientWidth, scroll: document.documentElement.scrollWidth }));
    assert.ok(bounds.scroll <= bounds.width + 1, "The actual version page must fit the changed desktop viewport.");
    await screenshot(page, "r09-version-page-" + width);
  }
  await page.setViewportSize({ width: 1600, height: 1000 });
}
async function formattedEditorSeed(page, expectedRaw) {
  const input = page.locator("#configuration-version-json");
  await expect(input).toBeEditable();
  const value = await input.inputValue();
  assert.ok(value.includes("\n") && value.includes("  \""), "Saved JSON opens as readable indented lines.");
  assert.equal(compactJson(value), compactJson(expectedRaw), "Initial editor formatting preserves every value token and member order.");
}

/** Native window bounds, not page.setViewportSize, must drive both dimensions. */
async function nativeWindowBounds(page, screenshot, expectedRaw) {
  assert.equal(page.viewportSize(), null, "This regression must not use viewport emulation.");
  const comparison = page.locator(".configuration-comparison");
  await expect(comparison).toHaveJSProperty("open", false);
  await comparison.locator("summary").click();
  await expect(comparison).toHaveJSProperty("open", true);
  await page.getByRole("button", { name: "Edit as new version", exact: true }).click();
  await expect(comparison).toHaveJSProperty("open", false);
  await formattedEditorSeed(page, expectedRaw);
  const cdp = await page.context().newCDPSession(page);
  const measurements = [], errors = [];
  let nativeWindow;
  try {
    const { targetInfo } = await cdp.send("Target.getTargetInfo");
    nativeWindow = await cdp.send("Browser.getWindowForTarget", { targetId: targetInfo.targetId });
    await cdp.send("Browser.setWindowBounds", { windowId: nativeWindow.windowId, bounds: { windowState: "normal" } });
    for (const requested of [{ width: 1000, height: 780 }, { width: 1440, height: 1080 }, { width: 1120, height: 880 }]) {
      const before = measurements.at(-1);
      await cdp.send("Browser.setWindowBounds", { windowId: nativeWindow.windowId, bounds: requested });
      await expect.poll(async () => {
        const frame = await cdp.send("Browser.getWindowBounds", { windowId: nativeWindow.windowId });
        const content = await page.evaluate(() => ({ width: innerWidth, height: innerHeight, outerWidth, outerHeight }));
        return Math.abs(frame.bounds.width - requested.width) <= 2 && Math.abs(frame.bounds.height - requested.height) <= 2
          && Math.abs(content.outerWidth - frame.bounds.width) <= 2 && Math.abs(content.outerHeight - frame.bounds.height) <= 2
          && content.outerWidth - content.width >= 0 && content.outerWidth - content.width <= 64
          && content.outerHeight - content.height >= 0 && content.outerHeight - content.height <= 256
          && (before === undefined || Math.abs(content.width - before.contentWidth) > 80 && Math.abs(content.height - before.contentHeight) > 80);
      }, { timeout: 10000, message: "The actual browser window and its un-emulated viewport must resize in width and height." }).toBe(true);
      const frame = (await cdp.send("Browser.getWindowBounds", { windowId: nativeWindow.windowId })).bounds;
      const metrics = await page.evaluate(() => {
        const rail = document.querySelector(".rail").getBoundingClientRect();
        const editor = document.querySelector("#configuration-version-json").getBoundingClientRect();
        const topbar = document.querySelector(".topbar").getBoundingClientRect();
        return { contentWidth: innerWidth, contentHeight: innerHeight, outerWidth, outerHeight, devicePixelRatio,
          documentWidth: document.documentElement.clientWidth, documentScrollWidth: document.documentElement.scrollWidth,
          railTop: rail.top, railBottom: rail.bottom, topbarRight: topbar.right, editorLeft: editor.left, editorRight: editor.right, editorHeight: editor.height };
      });
      assert.ok(metrics.outerWidth - metrics.contentWidth >= 0 && metrics.outerWidth - metrics.contentWidth <= 64,
        "No fixed smaller content viewport may leave a blank native window strip on the right.");
      assert.ok(metrics.outerHeight - metrics.contentHeight >= 0 && metrics.outerHeight - metrics.contentHeight <= 256,
        "Only native browser chrome, not an emulated smaller viewport, may consume the window height.");
      assert.ok(metrics.documentScrollWidth <= metrics.documentWidth + 1 && metrics.editorLeft >= 0 && metrics.editorRight <= metrics.documentWidth + 1);
      assert.ok(Math.abs(metrics.railTop) <= 1 && Math.abs(metrics.railBottom - metrics.contentHeight) <= 1
        && Math.abs(metrics.topbarRight - metrics.documentWidth) <= 1, "The shell fills the actual content viewport in both dimensions.");
      if (before !== undefined) {
        assert.equal(Math.sign(metrics.contentWidth - before.contentWidth), Math.sign(frame.width - before.windowWidth));
        assert.equal(Math.sign(metrics.contentHeight - before.contentHeight), Math.sign(frame.height - before.windowHeight));
      }
      measurements.push({ requestedWidth: requested.width, requestedHeight: requested.height, windowWidth: frame.width, windowHeight: frame.height, ...metrics });
      await screenshot(page, `r09-native-window-${requested.width}-${requested.height}`);
    }
    assert.ok(measurements[1].editorHeight > measurements[0].editorHeight + 1,
      "The editor's viewport-relative height responds to a taller actual window within its CSS clamp.");
  } catch (error) { errors.push(error); }
  finally {
    if (nativeWindow !== undefined) {
      try {
        await cdp.send("Browser.setWindowBounds", { windowId: nativeWindow.windowId, bounds: { windowState: "normal" } });
        const { windowState, ...bounds } = nativeWindow.bounds;
        await cdp.send("Browser.setWindowBounds", { windowId: nativeWindow.windowId, bounds });
        if (windowState !== undefined && windowState !== "normal") {
          await cdp.send("Browser.setWindowBounds", { windowId: nativeWindow.windowId, bounds: { windowState } });
        }
      } catch (error) { errors.push(error); }
    }
    try { await cdp.detach(); } catch (error) { errors.push(error); }
  }
  if (errors.length > 0) { throw new AggregateError(errors, "The owned native-window resize check did not finish cleanly."); }
  await page.getByRole("button", { name: "Cancel edits", exact: true }).click();
  await expect(page.locator("#configuration-version-json")).toHaveCount(0);
  await expect(comparison).toHaveJSProperty("open", false);
  return measurements;
}
async function get(page, url) {
  const response = await page.request.get(url);
  try { assert.equal(response.status(), 200); const reply = await response.json(); assert.equal(reply.state, "ready"); return reply.value; }
  finally { await response.dispose(); }
}
function sha(raw) { return createHash("sha256").update(raw, "utf8").digest("hex"); }
function compactJson(raw) {
  let result = "", quoted = false, escaped = false;
  for (const character of raw) {
    if (quoted) {
      result += character;
      if (escaped) { escaped = false; }
      else if (character === "\\") { escaped = true; }
      else if (character === '"') { quoted = false; }
    } else if (character === '"') { quoted = true; result += character; }
    else if (!" \t\r\n".includes(character)) { result += character; }
  }
  assert.equal(quoted, false); return result;
}
