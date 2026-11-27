// A continuation of the existing native Management/Auth.js fixture, with its real Shared-only assignment.
import assert from "node:assert/strict";
import { createHash } from "node:crypto";
import { expect } from "@playwright/test";
import { applicationRoute } from "../src/portal-routes.ts";
import { setConfigurationValue } from "../src/configuration-draft.ts";

export async function verifyConfigurationRepairsBrowser({ page, origin, applicationA, screenshot, cases }) {
  const configurationId = "shared.features", schemaVersion = "1";
  const base = origin + `/api/portal/applications/${applicationA}/configurations/${configurationId}`;
  const baseline = await get(page, base + "/details?schemaVersion=1&source=common");
  assert.equal(baseline.origin, "shared"); assert.equal(baseline.content.state, "present"); assert.equal(baseline.content.layer, "common");
  const shared = baseline.content.publication;
  const beforeMap = await get(page, base + "/assignments?schemaVersion=1");
  assert.equal(beforeMap.bindingId, baseline.bindingId); assert.equal(beforeMap.generation, "1"); assert.deepEqual(beforeMap.overrides, []);
  const summaries = await get(page, origin + `/api/portal/applications/${applicationA}/configuration-summaries?pageSize=100`);
  const summary = summaries.items.find(item => item.configurationId === configurationId && item.schemaVersion === schemaVersion);
  assert.ok(summary); assert.equal(summary.origin, "shared"); assert.equal(summary.bindingId, baseline.bindingId);
  assert.match(summary.modifiedAtUtc, /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{7}Z$/u);
  assert.equal(summary.modifiedAtUtc, shared.createdAtUtc);

  const mutations = [];
  const observe = request => {
    const url = new URL(request.url());
    if (url.origin === origin && request.method() !== "GET" && (url.pathname.includes("/configuration/") || url.pathname.includes("/configurations/"))) {
      mutations.push({ method: request.method(), path: url.pathname, key: request.headers()["idempotency-key"] });
    }
  };
  page.on("request", observe);
  try {
    await page.goto(origin + applicationRoute({ applicationId: applicationA, applicationName: "managed-a", tab: "configuration" }));
    const row = page.getByRole("row").filter({ has: page.getByRole("button", { name: configurationId, exact: true }) });
    await expect(row.locator("time")).toHaveAttribute("datetime", summary.modifiedAtUtc);
    const rowActions = page.waitForResponse(response => response.url() === base + "/row-actions?schemaVersion=1" && response.request().method() === "GET");
    await row.getByRole("button", { name: "Actions for shared.features", exact: true }).click();
    await value(await rowActions);
    const editorUrl = base + "/editor?schemaVersion=1&source=application";
    const firstEditor = page.waitForResponse(response => response.url() === editorUrl && response.request().method() === "GET");
    await row.getByRole("link", { name: "Override", exact: true }).click();
    const editing = await value(await firstEditor);
    assert.equal(editing.sharedOverrideSeed.state, "present");
    assert.equal(editing.sharedOverrideSeed.publication.publicationId, shared.publicationId);
    assert.equal(editing.sharedOverrideSeed.publication.payloadJson, shared.payloadJson);
    const editor = page.locator("#configuration-raw-json");
    await expect(editor).toBeEditable(); await expect(editor).toHaveValue(shared.payloadJson);
    cases.push("native-shared-override-default-copies-exact-assigned-json-with-authorized-date");

    const publishedRaw = setConfigurationValue(shared.payloadJson, ["Enabled"], "false");
    assert.notEqual(publishedRaw, shared.payloadJson);
    await editor.fill(publishedRaw);
    const refreshed = page.waitForResponse(response => response.url() === editorUrl && response.request().method() === "GET");
    await page.getByRole("button", { name: "Refresh current state", exact: true }).click(); await value(await refreshed);
    await expect(editor).toHaveValue(publishedRaw); await expect(editor).toBeEditable();
    await page.getByLabel("Change reason", { exact: true }).fill("Publish an explicit Application override copied from Shared JSON");
    await page.getByRole("button", { name: "Review Application JSON version", exact: true }).click();
    await expect(page.getByRole("heading", { name: "Review Application JSON version", exact: true })).toBeFocused();
    const publicationUrl = origin + `/api/portal/configuration/sources/${editing.source.sourceId}/publications`;
    const submitted = page.waitForRequest(request => request.url() === publicationUrl && request.method() === "POST");
    const completed = page.waitForResponse(response => response.url() === publicationUrl && response.request().method() === "POST");
    await page.getByRole("button", { name: "Publish Application JSON version", exact: true }).click();
    const request = await submitted;
    assert.equal(request.postDataJSON().payloadJson, publishedRaw);
    const publication = await value(await completed);
    assert.equal(publication.family, "management.configuration.source.publish"); assert.equal(publication.disposition, "applied");
    assert.equal(publication.originalCompletion, "committed"); assert.equal(publication.key, request.headers()["idempotency-key"]);
    assert.equal(publication.decision.sourceId, editing.source.sourceId);
    assert.equal(publication.decision.sha256, sha(publishedRaw));
    await expect(page.getByRole("heading", { name: "Publication committed — not assigned", exact: true })).toBeVisible();
    await expect(editor).toBeEditable();
    const bufferedRaw = setConfigurationValue(publishedRaw, ["Features", "Export"], "true");
    assert.notEqual(bufferedRaw, publishedRaw); await editor.fill(bufferedRaw); await expect(editor).toHaveValue(bufferedRaw);
    await expect(page.getByRole("button", { name: "Review Application JSON version", exact: true })).toBeDisabled();
    await expect(page.getByRole("button", { name: "Validate JSON", exact: true })).toBeDisabled();
    await expect(page.getByRole("heading", { name: "Publication committed — not assigned", exact: true })).toBeVisible();
    await expect(page.getByText("You can keep editing this local draft.", { exact: false })).toBeVisible();
    const originals = await page.evaluate(() => sessionStorage.getItem("cis.management.command-receipts.v1"));
    assert.ok(originals?.includes(publication.key));
    for (const secret of ["payloadJson", "CountrySearch", "sourceETag", "reason", "defaultsJson"]) { assert.equal(originals.includes(secret), false); }
    cases.push("native-known-original-retained-while-local-draft-remains-editable-and-new-publication-blocked");

    const commonAfter = await get(page, base + "/details?schemaVersion=1&source=common");
    assert.equal(commonAfter.origin, "shared"); assert.equal(commonAfter.content.state, "present");
    assert.equal(commonAfter.content.publication.publicationId, shared.publicationId);
    assert.equal(commonAfter.content.publication.payloadJson, shared.payloadJson);
    const afterMap = await get(page, base + "/assignments?schemaVersion=1");
    assert.deepEqual(afterMap, beforeMap, "Publishing one Application JSON version must not change the assigned release, generation or cohorts.");
    const immutable = await get(page, base + `/publication/${publication.decision.publicationId}?schemaVersion=1&source=application`);
    assert.equal(immutable.payloadJson, publishedRaw); assert.equal(immutable.sha256, sha(publishedRaw));
    assert.equal(mutations.filter(item => item.key !== undefined).length, 1);
    assert.equal(mutations.filter(item => item.path.endsWith("/releases") || item.path.endsWith("/assignments")).length, 0);
    await screenshot(page, "configuration-shared-copy-local-edit-known-original");
    cases.push("native-application-publication-does-not-change-shared-json-or-assignment");
    return { applicationId: applicationA, configurationId, schemaVersion, sourceId: editing.source.sourceId, bindingId: baseline.bindingId,
      commonPublicationId: shared.publicationId, defaultReleaseId: beforeMap.defaultReleaseId, generation: beforeMap.generation,
      publicationId: publication.decision.publicationId, publicationKey: publication.key,
      sharedSha256: sha(shared.payloadJson), publishedSha256: sha(publishedRaw), bufferedSha256: sha(bufferedRaw), modifiedAtUtc: summary.modifiedAtUtc };
  } finally { page.off("request", observe); }
}

function sha(raw) { return createHash("sha256").update(raw, "utf8").digest("hex"); }
async function value(response) {
  assert.equal(response.status(), 200); const reply = await response.json(); assert.equal(reply.state, "ready"); return reply.value;
}
async function get(page, url) {
  const response = await page.request.get(url);
  try { return await value(response); }
  finally { await response.dispose(); }
}
