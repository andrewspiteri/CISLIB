import test from "node:test";
import assert from "node:assert/strict";
import { ProtectedStateOwner, protectedStateLimits } from "../src/protected-state.ts";
import { CommandReceiptStore } from "../src/command-receipts.ts";

const now = 1800000000000;
const context = Object.freeze({ operatorId: "a".repeat(64), sessionAssociation: "b".repeat(64),
  serviceAssociation: "c".repeat(64), expiresAtMs: now + 3600000 });
const applicationId = "11111111-1111-1111-1111-111111111111";
const anotherId = "22222222-2222-2222-2222-222222222222";
const target = Object.freeze({ requestKey: "/api/applications/current?cursor=first", applicationId,
  environment: "development", definitionId: "country.settings", schemaVersion: "1" });

test("a protected readonly bootstrap captures only a current dated detached snapshot", async () => {
  const fixture = owner(undefined);
  assert.equal(fixture.state.view.context, undefined); assert.equal(fixture.state.view.canWrite, false);
  assert.equal(fixture.state.startDraft("{").reason, "not-ready");
  const original = { items: [{ displayName: "Allowed application" }] };
  const result = await fixture.state.request(async ticket => {
    assert.equal(ticket.context, undefined); assert.deepEqual(ticket.target, target); assert.ok(Object.isFrozen(ticket.target));
    return { state: "ready", context, value: original };
  });
  assert.equal(result.kind, "applied");
  original.items[0].displayName = "Changed elsewhere";
  const view = fixture.state.view;
  assert.equal(view.state, "ready"); assert.equal(view.canWrite, true);
  assert.equal(view.snapshot.receivedAtMs, now); assert.equal(view.snapshot.value.items[0].displayName, "Allowed application");
  assert.ok(Object.isFrozen(view)); assert.ok(Object.isFrozen(view.snapshot.value.items[0]));
  assert.equal(fixture.storage.value, null, "Protected values never enter receipt storage.");
});

test("each exact app environment definition schema and request selection suppresses old values at the same URL", async () => {
  for (const next of [
    { ...target, applicationId: anotherId }, { ...target, environment: "production" },
    { ...target, definitionId: "other.settings" }, { ...target, schemaVersion: "2" },
    { ...target, requestKey: target.requestKey + "&filter=other" }
  ]) {
    const fixture = owner(), entered = deferred(), release = deferred();
    await ready(fixture.state); fixture.state.startDraft("{}"); fixture.state.editDraft("{");
    let signal;
    const pending = fixture.state.request(async ticket => {
      signal = ticket.signal; entered.resolve(); await release.promise;
      return { state: "ready", context, value: { secret: "old target" } };
    });
    try {
      await entered.promise; fixture.state.selectTarget(next);
      assert.equal(signal.aborted, true); assert.equal(fixture.state.view.snapshot, undefined);
      assert.equal(fixture.state.view.draft, undefined); assert.equal(fixture.state.view.canWrite, false);
      assert.equal(fixture.state.view.pendingRequests, 1);
      await ready(fixture.state, { name: "New target" });
    } finally { release.resolve(); await pending; }
    assert.equal((await pending).kind, "stale"); assert.deepEqual(fixture.state.view.snapshot.value, { name: "New target" });
    assert.deepEqual(fixture.state.view.target, next); assert.equal(fixture.state.view.pendingRequests, 0);
  }
});

test("a newer request at the identical URL wins while the old callback remains actually owned", async () => {
  const fixture = owner(), entered = deferred(), release = deferred();
  const old = fixture.state.request(async ticket => {
    entered.resolve(ticket.signal); await release.promise; return { state: "ready", context, value: { version: "old" } };
  });
  try {
    const signal = await entered.promise;
    await ready(fixture.state, { version: "new" });
    assert.equal(signal.aborted, true); assert.equal(fixture.state.view.pendingRequests, 1);
    assert.equal(fixture.state.view.snapshot.value.version, "new");
  } finally { release.resolve(); await old; }
  assert.equal((await old).kind, "stale"); assert.equal(fixture.state.view.snapshot.value.version, "new");
});

test("signout and explicit unknown context invalidate a pending unknown bootstrap before it can adopt identity", async () => {
  for (const clear of [state => state.signOut(), state => state.setContext(undefined)]) {
    const fixture = owner(undefined), entered = deferred(), release = deferred();
    const old = fixture.state.request(async ticket => {
      entered.resolve(ticket.signal); await release.promise; return { state: "ready", context, value: { secret: "old" } };
    });
    try {
      const signal = await entered.promise; clear(fixture.state);
      assert.equal(signal.aborted, true); assert.equal(fixture.state.view.context, undefined);
      assert.equal(fixture.state.view.snapshot, undefined); assert.equal(fixture.state.view.canWrite, false);
      assert.equal(fixture.state.view.pendingRequests, 1);
    } finally { release.resolve(); await old; }
    assert.equal((await old).kind, "stale"); assert.equal(fixture.state.view.context, undefined);
  }
});

test("actual operator and service changes clear protected drafts snapshots and receipt references", async () => {
  for (const changed of [{ ...context, operatorId: "d".repeat(64) },
    { ...context, serviceAssociation: "d".repeat(64) }]) {
    const fixture = owner();
    const receipt = fixture.receipts.prepare({ family: "management.metadata.replace", target: { applicationId } });
    assert.equal(receipt.kind, "prepared");
    await ready(fixture.state); fixture.state.startDraft("{}"); fixture.state.editDraft("{");
    fixture.state.setContext(changed);
    assert.equal(fixture.state.view.snapshot, undefined); assert.equal(fixture.state.view.draft, undefined);
    assert.equal(fixture.state.view.navigationBlocked, false); assert.equal(fixture.state.view.canWrite, false);
    assert.equal(fixture.storage.value, null); assert.deepEqual(fixture.state.view.context, changed);
  }
});

test("same-user session renewal clears protected values and invalidates old work while retaining payload-free originals", async () => {
  const fixture = owner(), renewed = { ...context, sessionAssociation: "d".repeat(64) };
  const receipt = fixture.receipts.prepare({ family: "management.metadata.replace", target: { applicationId } }).receipt;
  const retained = fixture.storage.value;
  await ready(fixture.state); fixture.state.startDraft("{}"); fixture.state.editDraft("{");
  fixture.state.setContext(renewed);
  assert.equal(fixture.state.view.snapshot, undefined); assert.equal(fixture.state.view.draft, undefined);
  assert.equal(fixture.state.view.canWrite, false); assert.equal(fixture.storage.value, retained);
  assert.equal(fixture.receipts.load().receipts[0].key, receipt.key);
});

test("expiry or clock uncertainty clears protected values and suppresses pending replies without clock-based outcome inference", async () => {
  for (const invalidTime of [context.expiresAtMs, NaN, now - 1]) {
    const fixture = owner(), entered = deferred(), release = deferred();
    fixture.receipts.prepare({ family: "management.metadata.replace", target: { applicationId } });
    const retained = fixture.storage.value;
    await ready(fixture.state); fixture.state.startDraft("{}"); fixture.state.editDraft("{");
    const old = fixture.state.request(async ticket => {
      entered.resolve(ticket.signal); await release.promise; return { state: "ready", context, value: { secret: "late" } };
    });
    try {
      const signal = await entered.promise; fixture.time(invalidTime);
      const view = fixture.state.view;
      assert.equal(view.state, invalidTime === context.expiresAtMs ? "expired" : "clock-untrusted");
      assert.equal(view.context, undefined); assert.equal(view.snapshot, undefined); assert.equal(view.draft, undefined);
      assert.equal(view.canWrite, false); assert.equal(fixture.storage.value, invalidTime === context.expiresAtMs ? null : retained);
      assert.equal(signal.aborted, true);
      assert.equal(view.pendingRequests, 1);
    } finally { release.resolve(); await old; }
    assert.equal((await old).kind, "stale");
  }
});

test("permission loss clears protected content but preserves only same-user original references and joins lookup cancellation", async () => {
  const fixture = owner(), entered = deferred(), release = deferred();
  const receipt = fixture.receipts.prepare({ family: "management.metadata.replace", target: { applicationId } }).receipt;
  const retained = fixture.storage.value;
  await ready(fixture.state); fixture.state.startDraft("{}"); fixture.state.editDraft("{");
  const recovery = fixture.receipts.recover(receipt, async (_, signal) => {
    entered.resolve(signal); await release.promise; return { originalCompletion: "committed" };
  });
  try {
    const signal = await entered.promise; fixture.state.permissionLost();
    assert.equal(fixture.state.view.state, "denied"); assert.equal(fixture.state.view.snapshot, undefined);
    assert.equal(fixture.state.view.draft, undefined); assert.equal(fixture.state.view.canWrite, false);
    assert.equal(fixture.storage.value, retained); assert.equal(signal.aborted, true); assert.equal(fixture.receipts.recoveryBusy, true);
  } finally { release.resolve(); await recovery; }
  assert.equal((await recovery).kind, "canceled"); assert.equal(fixture.storage.value, retained);
  assert.equal(fixture.receipts.load().receipts[0].key, receipt.key);
});

test("a transient outage retains a dated same-scope snapshot and draft while writes stay disabled until a fresh read", async () => {
  const fixture = owner();
  await ready(fixture.state, { name: "Allowed before outage" });
  fixture.state.startDraft("{}"); fixture.state.editDraft("{");
  const timestamp = fixture.state.view.snapshot.receivedAtMs;
  await fixture.state.request(async () => ({ state: "unavailable" }));
  assert.equal(fixture.state.view.state, "stale"); assert.equal(fixture.state.view.canWrite, false);
  assert.equal(fixture.state.view.snapshot.receivedAtMs, timestamp);
  assert.equal(fixture.state.view.snapshot.value.name, "Allowed before outage");
  assert.equal(fixture.state.view.draft.text, "{"); assert.equal(fixture.state.view.navigationBlocked, true);
  assert.equal(fixture.state.editDraft('{"draft":').kind, "changed");
  assert.equal(fixture.keys(), 0); assert.equal(fixture.storage.value, null);
  await ready(fixture.state, { name: "Fresh after outage" });
  assert.equal(fixture.state.view.canWrite, true); assert.equal(fixture.state.view.snapshot.value.name, "Fresh after outage");
  fixture.state.transientUnavailable(); assert.equal(fixture.state.view.canWrite, false);
  assert.equal(fixture.state.view.state, "stale");
});

test("denied unavailable and invalid replies never turn bootstrap identity into a cached write permission", async () => {
  const fixture = owner(undefined);
  await fixture.state.request(async () => ({ state: "unavailable" }));
  assert.equal(fixture.state.view.context, undefined); assert.equal(fixture.state.view.state, "unavailable");
  await fixture.state.request(async () => ({ state: "denied", context }));
  assert.deepEqual(fixture.state.view.context, context); assert.equal(fixture.state.view.snapshot, undefined);
  assert.equal(fixture.state.view.canWrite, false);
  await ready(fixture.state);
  await fixture.state.request(async () => ({ state: "invalid", context }));
  assert.equal(fixture.state.view.state, "invalid"); assert.equal(fixture.state.view.snapshot, undefined);
  assert.equal(fixture.state.view.canWrite, false);
});

test("raw drafts admit incomplete editing within a strict memory bound and expose dirty navigation without persistence", async () => {
  const fixture = owner();
  await ready(fixture.state);
  assert.equal(fixture.state.startDraft("{}").kind, "changed");
  assert.equal(fixture.state.view.navigationBlocked, false);
  assert.equal(fixture.state.editDraft('{"incomplete":').kind, "changed");
  assert.equal(fixture.state.view.navigationBlocked, true);
  assert.equal(fixture.state.startDraft("replacement").reason, "dirty");
  assert.equal(fixture.state.editDraft("x".repeat(protectedStateLimits.maximumDraftBytes)).kind, "changed");
  const retained = fixture.state.view.draft;
  assert.equal(fixture.state.editDraft("x".repeat(protectedStateLimits.maximumDraftBytes + 1)).reason, "too-large");
  assert.equal(fixture.state.view.draft, retained);
  assert.equal(fixture.state.editDraft("é".repeat(protectedStateLimits.maximumDraftBytes / 2 + 1)).reason, "too-large");
  fixture.state.editDraft("{}"); assert.equal(fixture.state.view.navigationBlocked, false);
  fixture.state.discardDraft(); assert.equal(fixture.state.view.draft, undefined); assert.equal(fixture.storage.value, null);
});

test("unexpected callback faults propagate unchanged after actual settlement and cannot leave fresh writable state", async () => {
  const fixture = owner(), original = new Error("original source fault");
  await ready(fixture.state);
  await assert.rejects(fixture.state.request(async () => { throw original; }), value => value === original);
  assert.equal(fixture.state.view.pendingRequests, 0); assert.equal(fixture.state.view.state, "stale");
  assert.equal(fixture.state.view.canWrite, false); assert.deepEqual(fixture.state.view.snapshot.value, { name: "Allowed" });
  await assert.rejects(fixture.state.request(async () => ({ state: "made-up", context })), TypeError);
});

test("unsettled callback custody is bounded and cancellation does not manufacture settlement or a queued request", async () => {
  const fixture = owner(), release = deferred(), entered = [];
  const pending = [];
  try {
    for (let index = 0; index < protectedStateLimits.maximumPendingRequests; index++) {
      const start = deferred(); entered.push(start);
      pending.push(fixture.state.request(async ticket => {
        start.resolve(ticket.signal); await release.promise; return { state: "ready", context, value: { index } };
      }));
      await start.promise;
    }
    assert.equal(fixture.state.view.pendingRequests, protectedStateLimits.maximumPendingRequests);
    assert.deepEqual(await fixture.state.request(async () => assert.fail("Capacity must not invoke a queued callback.")),
      { kind: "blocked", reason: "busy" });
    fixture.state.cancelRequests();
    for (const start of entered) { assert.equal((await start.promise).aborted, true); }
    assert.equal(fixture.state.view.pendingRequests, protectedStateLimits.maximumPendingRequests);
  } finally { release.resolve(); await Promise.all(pending); }
  assert.equal(fixture.state.view.pendingRequests, 0); assert.equal(fixture.state.view.snapshot, undefined);
  for (const value of await Promise.all(pending)) { assert.equal(value.kind, "stale"); }
});

test("a reply from another actual same-user session clears old values without publishing its protected data or replacing originals", async () => {
  const fixture = owner(), actual = { ...context, sessionAssociation: "d".repeat(64) };
  fixture.receipts.prepare({ family: "management.metadata.replace", target: { applicationId } });
  const retained = fixture.storage.value;
  await ready(fixture.state); fixture.state.startDraft("{}"); fixture.state.editDraft("{");
  assert.equal((await fixture.state.request(async () => ({ state: "ready", context: actual, value: { private: "not published" } }))).kind, "stale");
  assert.deepEqual(fixture.state.view.context, actual); assert.equal(fixture.state.view.snapshot, undefined);
  assert.equal(fixture.state.view.draft, undefined); assert.equal(fixture.storage.value, retained);
  await ready(fixture.state, { name: "Fresh new session" }, actual);
  assert.equal(fixture.state.view.snapshot.value.name, "Fresh new session");
});

test("a bounded reported inventory snapshot larger than the ordinary response profile remains whole", async () => {
  const fixture = owner();
  const inventory = { registrationId: anotherId, applicationVersion: "1.0.0", receivedAtUtc: "2026-11-26T10:00:00.0000000Z",
    inventory: { sequence: "1", observedAtUtc: "2026-11-26T10:00:00.0000000Z",
      categories: [{ executionScope: "default", categoryId: "maintenance", displayName: "Maintenance" }],
      jobs: Array.from({ length: 1024 }, (_, index) => ({ executionScope: "default", jobId: "job-" + index,
        categoryId: "maintenance", displayName: "Job " + index, supportedModes: ["scheduled"], description: "x".repeat(1024) })) } };
  assert.ok(new TextEncoder().encode(JSON.stringify(inventory)).byteLength > 1024 * 1024);
  await ready(fixture.state, inventory);
  assert.equal(fixture.state.view.snapshot.value.inventory.jobs.length, 1024);
  assert.equal(fixture.state.view.snapshot.value.inventory.jobs.at(-1).description.length, 1024);
  assert.equal(fixture.state.view.canWrite, true);
});

test("target and snapshot bounds fail explicitly without changing target or exposing malformed values", async () => {
  const fixture = owner();
  for (const invalid of [{ ...target, applicationId: "not-a-uuid" }, { ...target, schemaVersion: "1.5" },
    { ...target, requestKey: "x".repeat(protectedStateLimits.maximumRequestKeyLength + 1) }, { ...target, token: "must-not-be-a-scope" }]) {
    assert.throws(() => fixture.state.selectTarget(invalid), TypeError);
    assert.deepEqual(fixture.state.view.target, target);
  }
  await assert.rejects(fixture.state.request(async () => ({ state: "ready", context,
    value: { oversized: "x".repeat(protectedStateLimits.maximumSnapshotBytes) } })), RangeError);
  assert.equal(fixture.state.view.snapshot, undefined); assert.equal(fixture.state.view.canWrite, false);
  assert.equal(fixture.state.view.pendingRequests, 0);
  await ready(fixture.state); fixture.state.selectTarget(undefined);
  assert.equal((await fixture.state.request(async () => assert.fail("No target must not dispatch."))).reason, "no-target");
  assert.equal(fixture.state.view.snapshot, undefined);
});

test("the exact request key accommodates existing encoded Unicode filters and cursor without normalization", () => {
  const fixture = owner();
  const query = new URLSearchParams({ query: "😀".repeat(128), environment: "😀".repeat(255), cursor: "x".repeat(2048) });
  const key = "/api/applications?" + query.toString();
  assert.ok(key.length > 4096);
  fixture.state.selectTarget({ requestKey: key });
  assert.equal(fixture.state.view.target.requestKey, key);
});

test("clock recovery permits only a fresh explicit readonly bootstrap before local editing resumes", async () => {
  const fixture = owner();
  const receipt = fixture.receipts.prepare({ family: "management.metadata.replace", target: { applicationId } }).receipt;
  const retained = fixture.storage.value;
  await ready(fixture.state);
  fixture.time(NaN); assert.equal(fixture.state.view.state, "clock-untrusted");
  assert.equal((await fixture.state.request(async () => assert.fail("Unknown clock must not dispatch."))).reason, "clock-untrusted");
  fixture.time(now); assert.equal(fixture.state.view.canWrite, false);
  await ready(fixture.state, { name: "Fresh after clock recovery" });
  assert.equal(fixture.state.view.canWrite, true); assert.equal(fixture.storage.value, retained);
  assert.equal(fixture.receipts.load().receipts[0].key, receipt.key); assert.equal(fixture.keys(), 1);
});

async function ready(state, value = { name: "Allowed" }, selectedContext = context) {
  const result = await state.request(async () => ({ state: "ready", context: selectedContext, value }));
  assert.equal(result.kind, "applied");
}
function owner(initialContext) {
  if (arguments.length === 0) { initialContext = context; }
  const storage = new MemoryStorage(); let time = now, keys = 0;
  const receipts = new CommandReceiptStore({ context: initialContext === undefined ? undefined : {
    operatorId: initialContext.operatorId, serviceAssociation: initialContext.serviceAssociation
  }, storage: () => storage, now: () => time,
    createKey: () => "00000000-0000-0000-0000-" + String(++keys).padStart(12, "0") });
  const state = new ProtectedStateOwner({ context: initialContext, receipts, now: () => time });
  state.selectTarget(target);
  return { state, receipts, storage, time: value => { time = value; }, keys: () => keys };
}
function deferred() { let resolve; const promise = new Promise(done => { resolve = done; }); return { promise, resolve }; }
class MemoryStorage {
  value = null;
  getItem() { return this.value; }
  setItem(_, value) { this.value = value; }
  removeItem() { this.value = null; }
}

