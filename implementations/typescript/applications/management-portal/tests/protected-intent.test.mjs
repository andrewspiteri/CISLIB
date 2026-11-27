import assert from "node:assert/strict";
import test from "node:test";
import { CommandReceiptStore } from "../src/command-receipts.ts";
import { ProtectedStateOwner } from "../src/protected-state.ts";

const app = "11111111-1111-4111-8111-111111111111";
const initial = 1800000000000;
const context = { operatorId: "a".repeat(64), serviceAssociation: "b".repeat(64), sessionAssociation: "c".repeat(64), expiresAtMs: initial + 600000 };
async function fixture() {
  let now = initial, writes = 0;
  const receipts = new CommandReceiptStore({ context, now: () => now, storage: () => ({ getItem: () => null, setItem: () => { writes++; }, removeItem: () => {} }) });
  const owner = new ProtectedStateOwner({ context, receipts, now: () => now });
  owner.selectTarget({ requestKey: "/configuration/settings", applicationId: app, definitionId: "settings", schemaVersion: "1" });
  await owner.request(async () => ({ state: "ready", context, value: { exact: "authorized binding" } }));
  return { owner, receipts, clock: value => { now = value; }, writes: () => writes };
}

test("reference-only intent blocks existing navigation without a fabricated payload or persisted draft", async () => {
  const { owner, writes } = await fixture();
  assert.equal(owner.view.draft, undefined);
  assert.equal(owner.setIntentDirty(true).kind, "changed");
  assert.equal(owner.view.intentDirty, true); assert.equal(owner.view.navigationBlocked, true);
  assert.equal(owner.view.draft, undefined); assert.equal(writes(), 0);
  owner.transientUnavailable(); assert.equal(owner.view.intentDirty, true); assert.equal(owner.view.canWrite, false);
  owner.permissionLost(); assert.equal(owner.view.intentDirty, false); assert.equal(owner.view.navigationBlocked, false);
});

test("reason/reference dirtiness neither changes exact JSON nor hides real payload dirtiness", async () => {
  const { owner } = await fixture();
  const raw = '{ "Large":9007199254740993, "Unicode":"Café 日本語", "Enabled":false, "Zero":0, "Null":null }';
  assert.equal(owner.startDraft(raw).kind, "changed");
  owner.setIntentDirty(true);
  assert.equal(owner.view.draft.text, raw); assert.equal(owner.view.draft.dirty, false);
  assert.equal(owner.startDraft("{}").reason, "dirty", "A fresh baseline cannot silently discard edited reference/reason intent.");
  owner.setIntentDirty(false); assert.equal(owner.view.navigationBlocked, false);
  owner.editDraft(raw + " "); owner.setIntentDirty(false);
  assert.equal(owner.view.navigationBlocked, true); assert.equal(owner.view.draft.text, raw + " ");
  owner.discardDraft(); assert.equal(owner.view.intentDirty, false); assert.equal(owner.view.navigationBlocked, false);
});

test("target session expiry and backward-clock invalidation clear non-payload intent with protected values", async () => {
  for (const invalidate of [owner => owner.selectTarget({ requestKey: "/configuration/other", applicationId: app }),
    owner => owner.setContext({ ...context, sessionAssociation: "d".repeat(64) }), owner => owner.signOut()]) {
    const { owner } = await fixture(); owner.setIntentDirty(true); invalidate(owner);
    assert.equal(owner.view.intentDirty, false); assert.equal(owner.view.navigationBlocked, false); assert.equal(owner.view.snapshot, undefined);
  }
  for (const expired of [true, false]) {
    const { owner, clock } = await fixture();
    clock(initial + 100); owner.setIntentDirty(true);
    clock(expired ? context.expiresAtMs : initial + 50);
    assert.equal(owner.view.intentDirty, false); assert.equal(owner.view.navigationBlocked, false); assert.equal(owner.view.snapshot, undefined);
    assert.equal(owner.view.state, expired ? "expired" : "clock-untrusted");
  }
});
