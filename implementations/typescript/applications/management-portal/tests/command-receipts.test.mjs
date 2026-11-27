import test from "node:test";
import assert from "node:assert/strict";
import { CommandReceiptStore, commandReceiptLimits } from "../src/command-receipts.ts";

const context = Object.freeze({ operatorId: "opaque-operator-a", serviceAssociation: "management-service-association-v1" });
const otherOperator = { ...context, operatorId: "opaque-operator-b" };
const otherService = { ...context, serviceAssociation: "another-management-service" };
const applicationId = "11111111-1111-1111-1111-111111111111";
const relatedId = "22222222-2222-2222-2222-222222222222";
const time = 1800000000000;
const metadata = { family: "management.metadata.replace", target: { applicationId } };
const intents = [
  metadata,
  { family: "management.configuration.create", target: { applicationId } },
  { family: "management.configuration.source.publish", target: { sourceId: relatedId } },
  { family: "management.configuration.release.create", target: { applicationId, configurationId: "shared.settings", schemaVersion: "9007199254740993" } },
  { family: "management.configuration.assignment.replace", target: { applicationId, configurationId: "shared.settings", schemaVersion: "1" } },
  { family: "management.scheduler.trigger.write", target: { applicationId, triggerId: relatedId } },
  { family: "management.scheduler.task.write", target: { applicationId, taskId: relatedId } },
  { family: "management.scheduler.run.write", target: { applicationId, runId: relatedId } }
];

test("finite exact families persist only minimal immutable original references before submission can start", () => {
  const fixture = owner();
  for (const intent of intents) {
    const result = fixture.store.prepare(intent);
    assert.equal(result.kind, "prepared");
    assert.equal(fixture.storage.value.includes(result.receipt.key), true, "The original key must already be retained.");
    assert.deepEqual(result.receipt.target, intent.target);
    assert.equal(result.receipt.family, intent.family);
    assert.ok(Object.isFrozen(result.receipt)); assert.ok(Object.isFrozen(result.receipt.target));
    assert.throws(() => { result.receipt.key = "replacement"; }, TypeError);
  }
  const loaded = fixture.store.load();
  assert.equal(loaded.kind, "ready"); assert.equal(loaded.receipts.length, 8); assert.ok(Object.isFrozen(loaded.receipts));
  const encoded = JSON.parse(fixture.storage.value);
  assert.deepEqual(Object.keys(encoded), ["version", "operatorId", "serviceAssociation", "receipts"]);
  for (const row of encoded.receipts) { assert.deepEqual(Object.keys(row), ["family", "target", "key", "createdAtMs"]); }
  const reloaded = new CommandReceiptStore({ context, storage: () => fixture.storage, now: () => time });
  assert.deepEqual(reloaded.load(), loaded);
});

test("unknown family mixed target and protected fields are rejected without key creation or storage mutation", () => {
  const fixture = owner();
  const invalid = [
    { family: "management.configuration.catalog.apply", target: { applicationId } },
    { family: "management.scheduler.event.submit", target: { applicationId } },
    { ...metadata, target: { applicationId, sourceId: relatedId } },
    { ...metadata, target: { applicationId: applicationId.toUpperCase().replace("1", "A") } },
    { family: "management.configuration.assignment.replace", target: { applicationId, configurationId: "..", schemaVersion: "1" } },
    { family: "management.configuration.release.create", target: { applicationId, configurationId: "settings", schemaVersion: 1 } },
    { family: "management.configuration.release.create", target: { applicationId, configurationId: "settings", schemaVersion: "9223372036854775808" } },
    ...["payload", "eTag", "claims", "token", "draft", "outcome"].map(name => ({ ...metadata, [name]: "must-not-be-retained" }))
  ];
  for (const intent of invalid) { assert.deepEqual(fixture.store.prepare(intent), { kind: "blocked", reason: "invalid-receipt" }); }
  assert.equal(fixture.keys(), 0); assert.equal(fixture.storage.value, null);
  const invalidContext = new CommandReceiptStore({ context: { ...context, token: "not-an-association" }, storage: () => fixture.storage });
  assert.deepEqual(invalidContext.prepare(metadata), { kind: "blocked", reason: "context-unavailable" });
});

test("strict bounded storage rejects malformed duplicate unknown and oversized data without deleting it", () => {
  const fixture = owner();
  prepared(fixture.store);
  const valid = fixture.storage.value;
  const baseline = JSON.parse(valid);
  const duplicate = structuredClone(baseline); duplicate.receipts.push({ ...duplicate.receipts[0], createdAtMs: time - 1 });
  const bad = [
    "", "[]", "null", valid + " trailing", valid.replace('"version":1', '"version":1,"\\u0076ersion":1'),
    JSON.stringify({ ...baseline, token: "must-not-be-read" }),
    JSON.stringify({ ...baseline, receipts: [{ ...baseline.receipts[0], target: { applicationId, payload: "private" } }] }),
    JSON.stringify({ ...baseline, receipts: [{ ...baseline.receipts[0], key: "." }] }),
    JSON.stringify({ ...baseline, receipts: [{ ...baseline.receipts[0], createdAtMs: 1.5 }] }),
    JSON.stringify(duplicate),
    JSON.stringify({ ...baseline, receipts: Array.from({ length: 17 }, (_, index) => ({ ...baseline.receipts[0], key: key(index + 50) })) }),
    valid + " ".repeat(commandReceiptLimits.maximumStorageBytes)
  ];
  for (const raw of bad) {
    fixture.storage.value = raw;
    assert.deepEqual(fixture.store.load(), { kind: "blocked", reason: "storage-invalid" });
    assert.equal(fixture.storage.value, raw);
  }
  fixture.storage.value = valid + " ".repeat(commandReceiptLimits.maximumStorageBytes - valid.length);
  assert.equal(fixture.store.load().kind, "ready");
  fixture.storage.value += " ";
  assert.equal(fixture.store.load().reason, "storage-invalid");
  const unicode = JSON.stringify({ ...baseline, operatorId: "é".repeat(256) });
  fixture.store.setContext({ ...context, operatorId: "é".repeat(256) });
  fixture.storage.value = unicode + " ".repeat(commandReceiptLimits.maximumStorageBytes - unicode.length);
  assert.ok(fixture.storage.value.length <= commandReceiptLimits.maximumStorageBytes);
  assert.equal(fixture.store.load().reason, "storage-invalid", "UTF-8 bytes also bound non-ASCII storage.");
});

test("capacity preserves all sixteen originals and never evicts an unresolved reference or generates a replacement", () => {
  const fixture = owner();
  for (let index = 0; index < 16; index++) { prepared(fixture.store); }
  const retained = fixture.storage.value;
  assert.deepEqual(fixture.store.prepare(metadata), { kind: "blocked", reason: "capacity" });
  assert.equal(fixture.keys(), 16); assert.equal(fixture.storage.value, retained);
  assert.equal(fixture.store.load().receipts.length, 16);
});

test("age expiry is explicit local-reference removal and a future clock blocks without backend-outcome inference", async () => {
  const fixture = owner();
  const receipt = prepared(fixture.store);
  fixture.time(time + commandReceiptLimits.maximumAgeMs - 1);
  assert.equal(fixture.store.load().receipts[0].key, receipt.key);
  fixture.time(time + commandReceiptLimits.maximumAgeMs);
  assert.deepEqual(fixture.store.prepare(metadata), { kind: "blocked", reason: "references-expired" });
  assert.equal(fixture.keys(), 1); assert.equal(fixture.storage.value, null);
  let lookups = 0;
  assert.deepEqual(await fixture.store.recover(receipt, async () => { lookups++; }), { kind: "blocked", reason: "not-retained" });
  assert.equal(lookups, 0); assert.equal(fixture.keys(), 1);
  const next = prepared(fixture.store);
  const retained = fixture.storage.value;
  fixture.time(next.createdAtMs - 1);
  assert.equal(fixture.store.load().reason, "clock-untrusted"); assert.equal(fixture.storage.value, retained);
  assert.equal(fixture.store.prepare(metadata).reason, "clock-untrusted"); assert.equal(fixture.keys(), 2);
  fixture.time(NaN); assert.equal(fixture.store.load().reason, "clock-untrusted");
});

test("storage denial quota and unverified writes never return a submit-eligible key", () => {
  const unavailable = new CommandReceiptStore({ context, storage: () => { throw new DOMException("Denied", "SecurityError"); } });
  assert.equal(unavailable.prepare(metadata).reason, "storage-unavailable");
  const fixture = owner();
  fixture.storage.failWrite = true;
  assert.equal(fixture.store.prepare(metadata).reason, "storage-unavailable");
  fixture.storage.failWrite = false; fixture.storage.ignoreWrite = true;
  assert.equal(fixture.store.prepare(metadata).reason, "storage-unavailable");
  fixture.storage.ignoreWrite = false;
  const original = prepared(fixture.store);
  const retained = fixture.storage.value;
  fixture.storage.failWrite = true;
  assert.equal(fixture.store.prepare(metadata).reason, "storage-unavailable");
  assert.equal(fixture.storage.value, retained); assert.equal(fixture.store.load().receipts[0].key, original.key);
  fixture.time(time + commandReceiptLimits.maximumAgeMs); fixture.storage.failRemove = true;
  assert.equal(fixture.store.load().reason, "storage-unavailable"); assert.equal(fixture.storage.value, retained);
});

test("operator or service mismatch is blocked and explicit local reset grants no command or native outcome", async () => {
  const fixture = owner();
  const receipt = prepared(fixture.store), retained = fixture.storage.value;
  for (const next of [otherOperator, otherService, undefined]) {
    fixture.store.setContext(next);
    const reason = next === undefined ? "context-unavailable" : "context-mismatch";
    assert.equal(fixture.store.load().reason, reason);
    assert.equal(fixture.store.prepare(metadata).reason, reason);
    assert.equal((await fixture.store.recover(receipt, async () => assert.fail("Foreign recovery cannot dispatch."))).reason, reason);
    assert.equal(fixture.storage.value, retained);
  }
  assert.equal(fixture.store.clearLocalReferences().kind, "cleared");
  assert.equal(fixture.storage.value, null); assert.equal(fixture.keys(), 1);
  assert.equal(fixture.store.prepare(metadata).reason, "context-unavailable");
});

test("uncertain not-found denied and committed lookup observations preserve the exact original without creating another key", async () => {
  const fixture = owner(), receipt = prepared(fixture.store), retained = fixture.storage.value;
  for (const value of [{ kind: "timeout" }, { kind: "http", status: 404 }, { kind: "http", status: 403 },
    { kind: "http", status: 200, originalCompletion: "committed", cleanupSucceeded: false }]) {
    const result = await fixture.store.recover(receipt, async selected => {
      assert.deepEqual(selected, receipt); assert.ok(Object.isFrozen(selected.target)); return value;
    });
    assert.equal(result.kind, "settled"); assert.deepEqual(result.value, value);
    assert.equal(fixture.storage.value, retained); assert.equal(fixture.keys(), 1);
  }
});

test("one selected recovery remains occupied through cancellation and actual callback cleanup", async () => {
  const fixture = owner(), receipt = prepared(fixture.store), entered = deferred(), release = deferred();
  let signal, complete = false;
  const pending = fixture.store.recover(receipt, async (_, selectedSignal) => {
    signal = selectedSignal; entered.resolve(); await release.promise; return { privateResult: "old" };
  });
  void pending.then(() => { complete = true; }, () => { complete = true; });
  try {
    await entered.promise; fixture.store.cancelRecovery();
    assert.equal(signal.aborted, true); assert.equal(fixture.store.recoveryBusy, true); assert.equal(complete, false);
    assert.equal(fixture.store.selectedRecovery.key, receipt.key);
    assert.deepEqual(await fixture.store.recover(receipt, async () => assert.fail("Busy selection cannot dispatch.")), { kind: "blocked", reason: "busy" });
    assert.equal(fixture.store.forgetExact(receipt).reason, "busy");
  } finally { release.resolve(); await pending; }
  assert.deepEqual(await pending, { kind: "canceled" }); assert.equal(fixture.store.recoveryBusy, false);
  assert.equal(fixture.store.load().receipts[0].key, receipt.key);
});

test("context changes and explicit local clear suppress late values without abandoning the selected recovery", async () => {
  for (const clear of [false, true]) {
    const fixture = owner(), receipt = prepared(fixture.store), entered = deferred(), release = deferred();
    const pending = fixture.store.recover(receipt, async (_, signal) => {
      entered.resolve(signal); await release.promise; return { privateResult: "must-not-cross-context" };
    });
    try {
      const signal = await entered.promise;
      if (clear) { assert.equal(fixture.store.clearLocalReferences().kind, "cleared"); }
      else { fixture.store.setContext(otherOperator); fixture.store.setContext(context); }
      assert.equal(signal.aborted, true); assert.equal(fixture.store.recoveryBusy, true); assert.equal(fixture.store.selectedRecovery, undefined);
      assert.equal((await fixture.store.recover(receipt, async () => assert.fail("Previous work still owns the slot."))).reason, "busy");
    } finally { release.resolve(); await pending; }
    assert.deepEqual(await pending, { kind: "stale" }); assert.equal(fixture.store.recoveryBusy, false);
    assert.equal(fixture.keys(), 1);
  }
});

test("expiry during a held recovery hides the aged reference and returns no late result", async () => {
  const fixture = owner(), receipt = prepared(fixture.store), entered = deferred(), release = deferred();
  const pending = fixture.store.recover(receipt, async () => { entered.resolve(); await release.promise; return "private"; });
  try {
    await entered.promise; fixture.time(time + commandReceiptLimits.maximumAgeMs);
    assert.equal(fixture.store.selectedRecovery, undefined); assert.equal(fixture.store.recoveryBusy, true);
  } finally { release.resolve(); await pending; }
  assert.deepEqual(await pending, { kind: "blocked", reason: "references-expired" });
});

test("original callback programming faults propagate after the real selection settles", async () => {
  const fixture = owner(), receipt = prepared(fixture.store), original = new Error("original sentinel");
  await assert.rejects(fixture.store.recover(receipt, async () => { throw original; }), error => error === original);
  assert.equal(fixture.store.recoveryBusy, false); assert.equal(fixture.store.load().receipts[0].key, receipt.key);
  assert.equal(fixture.keys(), 1);
});

test("forget is exact and key collisions or a context change during preparation never replace a retained reference", () => {
  const fixture = owner(), receipt = prepared(fixture.store), retained = fixture.storage.value;
  assert.equal(fixture.store.forgetExact({ ...receipt, target: { applicationId: relatedId } }).reason, "not-retained");
  assert.equal(fixture.storage.value, retained);
  const collision = new CommandReceiptStore({ context, storage: () => fixture.storage, now: () => time, createKey: () => receipt.key });
  assert.equal(collision.prepare(metadata).reason, "key-unavailable"); assert.equal(fixture.storage.value, retained);
  assert.equal(fixture.store.forgetExact(receipt).kind, "forgotten"); assert.equal(fixture.storage.value, null);
  let reentrant;
  reentrant = new CommandReceiptStore({ context, storage: () => fixture.storage, now: () => time,
    createKey: () => { reentrant.setContext(undefined); return key(90); } });
  assert.equal(reentrant.prepare(metadata).reason, "context-unavailable"); assert.equal(fixture.storage.value, null);
});

function prepared(store, intent = metadata) {
  const result = store.prepare(intent); assert.equal(result.kind, "prepared"); return result.receipt;
}
function owner() {
  const storage = new MemoryStorage(); let now = time, keys = 0;
  const store = new CommandReceiptStore({ context, storage: () => storage, now: () => now, createKey: () => key(++keys) });
  return { storage, store, time: value => { now = value; }, keys: () => keys };
}
function key(index) { return "00000000-0000-0000-0000-" + String(index).padStart(12, "0"); }
function deferred() { let resolve; const promise = new Promise(done => { resolve = done; }); return { promise, resolve }; }
class MemoryStorage {
  value = null;
  failWrite = false;
  failRemove = false;
  ignoreWrite = false;
  getItem() { return this.value; }
  setItem(_, value) { if (this.failWrite) { throw new DOMException("Full", "QuotaExceededError"); } if (!this.ignoreWrite) { this.value = value; } }
  removeItem() { if (this.failRemove) { throw new DOMException("Denied", "SecurityError"); } this.value = null; }
}
