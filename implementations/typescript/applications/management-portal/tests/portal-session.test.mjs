import test from "node:test";
import assert from "node:assert/strict";
import { CommandReceiptStore } from "../src/command-receipts.ts";
import { PortalSessionCoordinator } from "../src/portal-session.ts";
import { ProtectedStateOwner } from "../src/protected-state.ts";
import { capturePortalContext } from "../src/portal-context.ts";

const applicationId = "11111111-1111-4111-8111-111111111111";

test("accepted document leave discards all local edit owners without forgetting originals or ending actual callbacks", async () => {
  const f = fixture(), first = f.owner(), second = f.owner();
  await first.request(async () => {
    f.portal.observe(context("2"), 0, first);
    return { state: "ready", context: context("2"), value: "first authorized snapshot" };
  });
  second.selectTarget({ requestKey: "/api/portal/configuration" });
  await second.request(async () => ({ state: "ready", context: context("2"), value: "second authorized snapshot" }));
  first.startDraft("first baseline"); first.editDraft("first unsaved");
  second.startDraft("second baseline"); second.setIntentDirty(true);
  const original = f.receipts.prepare({ family: "management.metadata.replace", target: { applicationId } });
  assert.equal(original.kind, "prepared");
  const stored = f.raw(), held = Promise.withResolvers();
  const request = second.request(async ticket => {
    await held.promise;
    assert.equal(ticket.signal.aborted, false, "Discarding local text must not certify or cancel an unsettled callback.");
    return { state: "ready", context: context("2"), value: "actually settled snapshot" };
  });
  assert.equal(f.portal.navigationBlocked, true);
  f.portal.discardLocalDrafts();
  assert.equal(f.portal.navigationBlocked, false);
  assert.equal(first.view.draft, undefined); assert.equal(second.view.intentDirty, false);
  assert.equal(first.view.snapshot.value, "first authorized snapshot");
  assert.equal(second.view.pendingRequests, 1); assert.equal(f.raw(), stored);
  assert.equal(f.receipts.load().receipts[0].key, original.receipt.key);
  held.resolve(); await request;
  assert.equal(second.view.pendingRequests, 0); assert.equal(f.raw(), stored);
});
function context(session, operator = "1") {
  return { operatorId: operator.repeat(64), sessionAssociation: session.repeat(64), serviceAssociation: "a".repeat(64), expiresAtMs: 10000 };
}
function fixture() {
  let now = 1000;
  let raw = null;
  const storage = { getItem: () => raw, setItem: (_, value) => { raw = value; }, removeItem: () => { raw = null; } };
  const receipts = new CommandReceiptStore({ context: undefined, storage: () => storage, now: () => now,
    createKey: () => "22222222-2222-4222-8222-222222222222" });
  const portal = new PortalSessionCoordinator(receipts, () => now);
  const owner = () => {
    const value = new ProtectedStateOwner({ context: undefined, receipts, now: () => now });
    value.selectTarget({ requestKey: "/api/portal/applications" });
    portal.register(value);
    return value;
  };
  return { portal, receipts, owner, raw: () => raw, advance: value => { now = value; } };
}

test("first accepted bootstrap advances the shared epoch and invalidates a delayed foreign bootstrap", async () => {
  const f = fixture();
  const old = f.owner();
  const current = f.owner();
  const delayed = Promise.withResolvers();
  const started = Promise.withResolvers();
  const epoch = f.portal.getSnapshot().epoch;
  const pending = old.request(async () => {
    started.resolve();
    await delayed.promise;
    const observed = f.portal.observe(context("2", "2"), epoch, old);
    return observed.accepted ? { state: "ready", context: context("2", "2"), value: "old-private-value" } : { state: "unavailable" };
  });
  await started.promise;
  await current.request(async () => {
    assert.equal(f.portal.observe(context("3"), epoch, current).accepted, true);
    return { state: "ready", context: context("3"), value: "current-private-value" };
  });
  assert.equal(f.portal.getSnapshot().epoch, epoch + 1);
  delayed.resolve();
  assert.equal((await pending).kind, "stale");
  assert.equal(old.view.snapshot, undefined);
  assert.equal(current.view.snapshot.value, "current-private-value");
  assert.equal(f.portal.getSnapshot().context.operatorId, "1".repeat(64));
});

test("session-only renewal clears all views before publish while retaining same-user original references", async () => {
  const f = fixture();
  const owner = f.owner();
  await owner.request(async () => {
    f.portal.observe(context("2"), 0, owner);
    return { state: "ready", context: context("2"), value: { private: "allowed" } };
  });
  owner.startDraft("initial");
  owner.editDraft("unsaved");
  const saved = f.receipts.prepare({ family: "management.metadata.replace", target: { applicationId } });
  assert.equal(saved.kind, "prepared");
  const original = f.raw();
  let observedCleared = false;
  const stop = f.portal.subscribe(() => { observedCleared = owner.view.snapshot === undefined && owner.view.draft === undefined; });
  f.portal.observe(context("3"), f.portal.getSnapshot().epoch, owner);
  assert.equal(observedCleared, true);
  assert.equal(f.raw(), original);
  assert.equal(f.receipts.load().receipts[0].key, saved.receipt.key);
  stop();
  f.portal.signOut();
  assert.equal(f.raw(), null);
  assert.equal(f.portal.getSnapshot().blocked, true);
});

test("expiry blocks the document while an admitted callback stays owned until actual settlement", async () => {
  const f = fixture();
  const owner = f.owner();
  await owner.request(async () => {
    f.portal.observe(context("2"), 0, owner);
    return { state: "ready", context: context("2"), value: "allowed" };
  });
  const delayed = Promise.withResolvers();
  const started = Promise.withResolvers();
  const pending = owner.request(async ticket => { started.resolve(); await delayed.promise; return { state: "ready", context: ticket.context, value: "late" }; });
  await started.promise;
  f.advance(10000);
  f.portal.checkExpiry();
  assert.equal(f.portal.getSnapshot().blocked, true);
  assert.equal(owner.view.snapshot, undefined);
  assert.equal(owner.view.pendingRequests, 1);
  delayed.resolve();
  assert.equal((await pending).kind, "stale");
  assert.equal(owner.view.pendingRequests, 0);
});

test("public context is a closed opaque association envelope rather than arbitrary session data", () => {
  const valid = context("2");
  assert.deepEqual(capturePortalContext(valid), valid);
  assert.equal(capturePortalContext({ ...valid, access_token: "private" }), undefined);
  assert.equal(capturePortalContext({ ...valid, sessionAssociation: "raw-session-ticket" }), undefined);
  assert.equal(capturePortalContext({ ...valid, expiresAtMs: NaN }), undefined);
});

test("a corrected clock needs an explicit fresh bootstrap and retains the original receipt", async () => {
  const f = fixture();
  const owner = f.owner();
  await owner.request(async () => {
    f.portal.observe(context("2"), 0, owner);
    return { state: "ready", context: context("2"), value: "allowed" };
  });
  const saved = f.receipts.prepare({ family: "management.metadata.replace", target: { applicationId } });
  assert.equal(saved.kind, "prepared");
  const original = f.raw();
  f.advance(NaN);
  f.portal.checkExpiry();
  const blocked = f.portal.getSnapshot();
  assert.equal(blocked.blockReason, "clock");
  assert.equal(f.portal.resumeClock(), false);
  f.advance(2000);
  f.portal.checkExpiry();
  assert.equal(f.portal.getSnapshot(), blocked, "Focus alone cannot restore protected state or create a retry loop.");
  assert.equal(f.portal.resumeClock(), true);
  assert.equal(owner.view.snapshot, undefined);
  await owner.request(async () => {
    f.portal.observe(context("2"), f.portal.getSnapshot().epoch, owner);
    return { state: "ready", context: context("2"), value: "fresh" };
  });
  assert.equal(owner.view.snapshot.value, "fresh");
  assert.equal(f.raw(), original);
});
