// Shared-copy, draft and exact Save recovery through the current version page.
import assert from "node:assert/strict";
import { createHash } from "node:crypto";
import { expect } from "@playwright/test";
import { applicationRoute } from "../src/portal-routes.ts";
import { formatConfigurationJson, setConfigurationValue } from "../src/configuration-draft.ts";
import { commitConfigurationStage, recoverConfigurationStage } from "./native-r08-checks.mjs";

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
    await row.getByRole("link", { name: "Override", exact: true }).click();
    const versionRow = page.locator(`[data-version-id="${beforeMap.defaultReleaseId}"]`);
    await versionRow.locator(".version-title").click();
    await expect(page.locator(".configuration-selected-version .configuration-json-readonly")).toBeVisible();
    const editorUrl = base + "/editor?schemaVersion=1&source=application";
    const firstEditor = page.waitForResponse(response => response.url() === editorUrl && response.request().method() === "GET");
    await page.getByRole("button", { name: "Edit as new version", exact: true }).click();
    const editing = await value(await firstEditor);
    assert.equal(editing.sharedOverrideSeed.state, "present");
    assert.equal(editing.sharedOverrideSeed.publication.publicationId, shared.publicationId);
    assert.equal(editing.sharedOverrideSeed.publication.payloadJson, shared.payloadJson);
    const editor = page.locator("#configuration-version-json"), formattedSeed = formatConfigurationJson(shared.payloadJson);
    assert.equal(formattedSeed.valid, true);
    await expect(editor).toBeEditable(); await expect(editor).toHaveValue(formattedSeed.json);
    cases.push("native-shared-override-copies-and-formats-exact-assigned-json-with-authorized-date");

    const publishedRaw = setConfigurationValue(shared.payloadJson, ["Enabled"], "false");
    assert.notEqual(publishedRaw, shared.payloadJson);
    await editor.fill(publishedRaw);
    const refreshed = page.waitForResponse(response => response.url() === editorUrl && response.request().method() === "GET");
    await page.getByRole("button", { name: "Refresh", exact: true }).click(); await value(await refreshed);
    await expect(editor).toHaveValue(publishedRaw); await expect(editor).toBeEditable();
    const note = "Save an explicit Application override copied from Shared JSON";
    await page.getByLabel("Version note", { exact: true }).fill(note);
    const publicationUrl = origin + `/api/portal/configuration/sources/${editing.source.sourceId}/publications`;
    const publication = await commitConfigurationStage(page, publicationUrl, "POST", "Save version", "management.configuration.source.publish", true,
      body => assert.equal(body.payloadJson, publishedRaw));
    assert.equal(publication.decision.sourceId, editing.source.sourceId);
    assert.equal(publication.decision.sha256, sha(publishedRaw));
    const bufferedRaw = setConfigurationValue(publishedRaw, ["Features", "Export"], "true");
    assert.notEqual(bufferedRaw, publishedRaw);
    await expect(editor).toBeEditable(); await editor.fill(bufferedRaw); await expect(editor).toHaveValue(bufferedRaw);
    await expect(page.getByRole("button", { name: "Save version", exact: true })).toBeDisabled();
    await recoverConfigurationStage(page, origin + `/api/portal/configuration/sources/${editing.source.sourceId}/publication-operations/${publication.key}`,
      "Recover original Save result", publication.key);
    await expect(editor).toHaveValue(bufferedRaw);
    const release = await commitConfigurationStage(page, base + "/releases?schemaVersion=1", "POST", "Continue incomplete Save", "management.configuration.release.create", false,
      body => {
        assert.deepEqual(body.commonSource, { state: "present", publicationId: shared.publicationId });
        assert.deepEqual(body.applicationSource, { state: "present", publicationId: publication.decision.publicationId });
      });
    await expect(editor).toBeEditable(); await expect(editor).toHaveValue(bufferedRaw);
    await expect(page.getByRole("button", { name: "Save version", exact: true })).toBeDisabled();
    await expect(page.getByText("Version saved. Current local edits and request cleanup remain separate facts below.", { exact: true })).toBeVisible();
    const originals = await page.evaluate(() => sessionStorage.getItem("cis.management.command-receipts.v1"));
    assert.ok(originals?.includes(publication.key) && originals.includes(release.key));
    for (const secret of ["payloadJson", "CountrySearch", "sourceETag", "reason", "defaultsJson"]) { assert.equal(originals.includes(secret), false); }
    cases.push("native-known-originals-retained-while-buffered-draft-remains-editable-and-new-save-blocked");

    const commonAfter = await get(page, base + "/details?schemaVersion=1&source=common");
    assert.equal(commonAfter.origin, "shared"); assert.equal(commonAfter.content.state, "present");
    assert.equal(commonAfter.content.publication.publicationId, shared.publicationId);
    assert.equal(commonAfter.content.publication.payloadJson, shared.payloadJson);
    assert.deepEqual(await get(page, base + "/assignments?schemaVersion=1"), beforeMap, "Save must not deploy its new immutable pair or alter generation/cohorts.");
    const immutable = await get(page, base + `/publication/${publication.decision.publicationId}?schemaVersion=1&source=application`);
    assert.equal(immutable.payloadJson, publishedRaw); assert.equal(immutable.sha256, sha(publishedRaw));
    const saved = await get(page, base + `/versions/${release.decision.releaseId}?schemaVersion=1`);
    assert.equal(saved.release.commonSource.publicationId, shared.publicationId);
    assert.equal(saved.release.applicationSource.publicationId, publication.decision.publicationId);
    assert.equal(saved.content.publication.payloadJson, publishedRaw);
    assert.equal(mutations.filter(item => item.key !== undefined).length, 2);
    assert.equal(mutations.filter(item => item.path.endsWith("/releases")).length, 1);
    assert.equal(mutations.filter(item => item.path.endsWith("/assignments")).length, 0);
    await screenshot(page, "configuration-shared-copy-buffered-draft-known-save-originals");
    cases.push("native-application-save-pins-exact-shared-and-application-publications-without-changing-deployment");
    return { applicationId: applicationA, configurationId, schemaVersion, sourceId: editing.source.sourceId, bindingId: baseline.bindingId,
      commonPublicationId: shared.publicationId, defaultReleaseId: beforeMap.defaultReleaseId, generation: beforeMap.generation,
      publicationId: publication.decision.publicationId, publicationKey: publication.key, releaseId: release.decision.releaseId, releaseKey: release.key,
      sharedSha256: sha(shared.payloadJson), publishedSha256: sha(publishedRaw), bufferedSha256: sha(bufferedRaw), modifiedAtUtc: summary.modifiedAtUtc };
  } finally { page.off("request", observe); }
}

function sha(raw) { return createHash("sha256").update(raw, "utf8").digest("hex"); }
async function value(response) { assert.equal(response.status(), 200); const reply = await response.json(); assert.equal(reply.state, "ready"); return reply.value; }
async function get(page, url) { const response = await page.request.get(url); try { return await value(response); } finally { await response.dispose(); } }
