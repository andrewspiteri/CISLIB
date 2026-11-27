import assert from "node:assert/strict";
import test from "node:test";
import { CommandReceiptStore } from "../src/command-receipts.ts";
import { configurationEditingAccess, initializeConfigurationEditorDraft } from "../src/configuration-editing.ts";
import { formatConfigurationJson } from "../src/configuration-draft.ts";
import { projectConfigurationEditor } from "../src/configuration-editor-model.ts";
import { configurationReadTarget } from "../src/configuration-recovery.ts";
import { ProtectedStateOwner } from "../src/protected-state.ts";

const app = "11111111-1111-4111-8111-111111111111", binding = "22222222-2222-4222-8222-222222222222";
const application = "33333333-3333-4333-8333-333333333333", common = "44444444-4444-4444-8444-444444444444";
const sharedPublication = "55555555-5555-4555-8555-555555555555", appPublication = "66666666-6666-4666-8666-666666666666";
const now = 1800000000000;
const session = { operatorId: "a".repeat(64), sessionAssociation: "b".repeat(64), serviceAssociation: "c".repeat(64), expiresAtMs: now + 600000 };
const sharedRaw = String.raw` { "Large":9007199254740993,"Ratio":1.2300e+4,"Text":"日本語 🌍 \u00e9","False":false,"Zero":0,"Null":null } `;
const current = { ownerState: "ready", blocked: false, busy: false, fresh: true, originalsRetained: false, receiptStorageReady: true };

function publication(kind, payloadJson = sharedRaw) {
  return { publicationId: kind === "common" ? sharedPublication : appPublication, sourceId: kind === "common" ? common : application,
    configurationId: "shared.features", schemaVersion: "1", environment: "Development", kind,
    ...(kind === "application" ? { applicationId: app } : {}), sha256: "d".repeat(64), payloadJson,
    createdAtUtc: "2026-11-26T12:34:56.1234567Z", actor: "opaque", reason: "Existing assigned JSON" };
}
function snapshot() {
  return { context: { applicationId: app, application: "managed-a", environment: "Development", configurationId: "shared.features", schemaVersion: "1",
    bindingId: binding, hostApplicationName: "FeatureFlags", selectedSource: "application", shape: { kind: "object", nullable: false, members: [] },
    applicationSource: { state: "available", sourceId: application }, commonSource: { state: "available", sourceId: common }, defaults: { state: "available", defaultsJson: '{"NotShared":"default"}' } },
    source: { sourceId: application, configurationId: "shared.features", schemaVersion: "1", environment: "Development", applicationId: app,
      kind: "application", eTag: '"source1"', publications: { items: [] } }, content: { state: "absent" },
    baselineRelease: { releaseId: "77777777-7777-4777-8777-777777777777", commonSource: { state: "present", publicationId: sharedPublication }, applicationSource: { state: "absent" } },
    actions: { state: "available", source: { resourceId: application, actions: [{ operationId: "publishConfigurationSource", supported: true, allowed: true }] },
      binding: { resourceId: binding, actions: [] } }, sharedOverrideSeed: { state: "present", publication: publication("common") } };
}
async function fixture(value = snapshot()) {
  let stored = null;
  const receipts = new CommandReceiptStore({ context: { operatorId: session.operatorId, serviceAssociation: session.serviceAssociation }, now: () => now,
    createKey: () => "88888888-8888-4888-8888-888888888888", storage: () => ({ getItem: () => stored, setItem: (_key, raw) => { stored = raw; }, removeItem: () => { stored = null; } }) });
  const owner = new ProtectedStateOwner({ context: session, receipts, now: () => now });
  owner.selectTarget(configurationReadTarget({ applicationId: app, configurationId: "shared.features", schemaVersion: "1", view: "application" }));
  await owner.request(async () => ({ state: "ready", context: session, value }));
  return { owner, receipts, stored: () => stored };
}

test("a new Application override copies the exact assigned Shared text once and protects its dirty intent", async () => {
  const state = await fixture();
  assert.equal(initializeConfigurationEditorDraft(state.owner).kind, "started");
  assert.equal(state.owner.view.draft.text, sharedRaw);
  assert.equal(state.owner.view.intentDirty, true); assert.equal(state.owner.view.navigationBlocked, true);
  const edited = sharedRaw.replace("9007199254740993", "9007199254740995");
  assert.equal(state.owner.editDraft(edited).kind, "changed");
  const refreshed = snapshot(), laterId = "99999999-9999-4999-8999-999999999999";
  refreshed.baselineRelease.commonSource.publicationId = laterId;
  refreshed.sharedOverrideSeed.publication = { ...publication("common", '{"Later":true}'), publicationId: laterId };
  await state.owner.request(async () => ({ state: "ready", context: session, value: refreshed }));
  assert.equal(initializeConfigurationEditorDraft(state.owner).kind, "retained");
  assert.equal(state.owner.view.draft.text, edited, "A refresh must not copy over any retained draft.");
});

test("existing assigned Application JSON wins over Shared and a different publication cannot masquerade as its baseline", async () => {
  const value = snapshot(), raw = String.raw`{"Own":9007199254740997,"Escape":"\u65e5"}`;
  value.content = { state: "present", publication: publication("application", raw) };
  value.baselineRelease.applicationSource = { state: "present", publicationId: appPublication };
  const state = await fixture(value);
  assert.equal(initializeConfigurationEditorDraft(state.owner).kind, "started"); assert.equal(state.owner.view.draft.text, raw);
  assert.equal(state.owner.view.intentDirty, false);
  value.content.publication.publicationId = sharedPublication;
  const foreign = await fixture(value);
  assert.equal(initializeConfigurationEditorDraft(foreign.owner).kind, "blocked"); assert.equal(foreign.owner.view.draft, undefined);
});

test("Shared copying refuses unavailable authority or a foreign assigned/source tuple without defaults or empty-object substitution", async () => {
  for (const seed of [undefined, { state: "redacted", message: "Shared ContentRead denied" }, { state: "unavailable", message: "Read unavailable" },
    { state: "present", publication: { ...publication("common"), publicationId: appPublication } },
    { state: "present", publication: { ...publication("common"), sourceId: application } },
    { state: "present", publication: { ...publication("common"), applicationId: app } },
    { state: "present", publication: { ...publication("common"), environment: "Other" } },
    { state: "present", publication: { ...publication("common"), configurationId: "foreign" } },
    { state: "present", publication: { ...publication("common"), schemaVersion: "2" } }]) {
    const value = { ...snapshot(), sharedOverrideSeed: seed }, state = await fixture(value);
    assert.equal(initializeConfigurationEditorDraft(state.owner).kind, "blocked");
    assert.equal(state.owner.view.draft, undefined, "Neither {} nor declared defaults are a Shared copy.");
  }
});

test("local JSON remains editable with a retained unknown original or stale read while every new dispatch stays blocked", async () => {
  const state = await fixture(); initializeConfigurationEditorDraft(state.owner);
  const prepared = state.receipts.prepare({ family: "management.configuration.source.publish", target: { sourceId: application } });
  assert.equal(prepared.kind, "prepared"); const retained = state.stored();
  state.owner.transientUnavailable();
  const access = configurationEditingAccess(state.owner.view.snapshot.value, { ...current, ownerState: state.owner.view.state, fresh: false, originalsRetained: true });
  assert.deepEqual(access, { canEdit: true, canPublish: false });
  const edited = sharedRaw.replace('"False":false', '"False":true');
  assert.equal(state.owner.editDraft(edited).kind, "changed");
  const formatted = formatConfigurationJson(edited); assert.equal(formatted.valid, true);
  assert.equal(state.owner.editDraft(formatted.json).kind, "changed"); assert.equal(state.owner.view.navigationBlocked, true);
  assert.equal(state.stored(), retained); assert.equal(state.receipts.load().receipts[0].key, prepared.receipt.key);
  assert.deepEqual(configurationEditingAccess(snapshot(), { ...current, originalsRetained: true }), { canEdit: true, canPublish: false });
  assert.deepEqual(configurationEditingAccess(snapshot(), { ...current, receiptStorageReady: false }), { canEdit: true, canPublish: false });
  assert.deepEqual(configurationEditingAccess(snapshot(), current), { canEdit: true, canPublish: true });
});

test("editing never grants authority for a readonly source, lost session, wrong action resource or unavailable protected values", () => {
  for (const value of [undefined, { ...snapshot(), context: { ...snapshot().context, selectedSource: "common" } },
    { ...snapshot(), content: { state: "unavailable", message: "Content protected" } },
    { ...snapshot(), context: { ...snapshot().context, defaults: { state: "redacted" } } },
    { ...snapshot(), actions: { ...snapshot().actions, source: { resourceId: common, actions: snapshot().actions.source.actions } } },
    { ...snapshot(), actions: { state: "unavailable", message: "No native permission" } }]) {
    assert.deepEqual(configurationEditingAccess(value, current), { canEdit: false, canPublish: false });
  }
  for (const state of [{ blocked: true }, { busy: true }, { ownerState: "denied" }, { ownerState: "expired" }]) {
    assert.deepEqual(configurationEditingAccess(snapshot(), { ...current, ...state }), { canEdit: false, canPublish: false });
  }
});

test("no assigned Shared layer starts an explicit empty draft while protected context never supplies defaults as payload", async () => {
  const value = snapshot(); delete value.baselineRelease; delete value.sharedOverrideSeed; value.content = { state: "unselected", message: "Unassigned" };
  const state = await fixture(value); assert.equal(initializeConfigurationEditorDraft(state.owner).kind, "started");
  assert.equal(state.owner.view.draft.text, "{}"); assert.equal(state.owner.view.intentDirty, false);
  state.owner.setContext({ ...session, sessionAssociation: "e".repeat(64) });
  assert.equal(state.owner.view.draft, undefined, "An actual session change clears protected copied values.");
});

test("the browser projection carries an exact Shared seed without copying future private fields or redacted payload", () => {
  const value = snapshot(); value.sharedOverrideSeed.publication.futureCredential = "secret"; value.sharedOverrideSeed.defaultsJson = "secret";
  const projected = projectConfigurationEditor(value);
  assert.equal(projected.sharedOverrideSeed.publication.payloadJson, sharedRaw);
  assert.equal(projected.sharedOverrideSeed.publication.futureCredential, undefined); assert.equal(projected.sharedOverrideSeed.defaultsJson, undefined);
  const redacted = projectConfigurationEditor({ ...value, sharedOverrideSeed: { state: "redacted", message: "Not permitted", publication: publication("common") } });
  assert.deepEqual(redacted.sharedOverrideSeed, { state: "redacted", message: "Not permitted" });
});
