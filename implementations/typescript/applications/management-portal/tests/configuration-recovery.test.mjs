import assert from "node:assert/strict";
import test from "node:test";
import { CommandReceiptStore } from "../src/command-receipts.ts";
import { ProtectedStateOwner } from "../src/protected-state.ts";
import { configurationReadTarget, recoverConfigurationOriginal } from "../src/configuration-recovery.ts";

const applicationId = "11111111-1111-1111-1111-111111111111", sourceId = "22222222-2222-2222-2222-222222222222";
const now = 1800000000000;
const context = { operatorId: "a".repeat(64), serviceAssociation: "b".repeat(64), sessionAssociation: "c".repeat(64), expiresAtMs: now + 600000 };

test("a late independent Configuration read selection reproduces cancellation when it was not prepared before lookup", async () => {
  const state = fixture("application");
  const entered = deferred(), held = deferred();
  let signal;
  const pending = state.receipts.recover(state.receipt, async (_receipt, value) => { signal = value; entered.resolve(); await held.promise; });
  try {
    await entered.promise;
    state.readOwner.selectTarget(configurationReadTarget(state.selection));
    assert.equal(signal.aborted, true, "The real shared owner deliberately cancels a recovery on later target selection.");
    assert.equal(state.receipts.recoveryBusy, true, "Canceled work retains native callback custody.");
  } finally { held.resolve(); }
  assert.equal((await pending).kind, "canceled");
});

for (const view of ["application", "assignment"]) {
  test(`${view} original lookup survives delayed same-view background initialization without a read prefetch`, async () => {
    const state = fixture(view), entered = deferred(), held = deferred();
    let signal, invoked = 0;
    const pending = recoverConfigurationOriginal(state.readOwner, state.commandOwner, state.receipts, state.receipt, state.selection, true,
      async (url, original, recoverySignal) => {
        invoked++; signal = recoverySignal;
        state.commandOwner.selectTarget({ requestKey: url + "#lookup", ...(view === "application" ? {} : { applicationId, definitionId: "shared.settings", schemaVersion: "1" }) });
        assert.equal(signal.aborted, false); assert.equal(original.key, state.receipt.key);
        await state.commandOwner.request(async ticket => {
          assert.equal(ticket.signal.aborted, false); entered.resolve(); await held.promise;
          assert.equal(signal.aborted, false); return { state: "ready", context, value: { original: original.key } };
        });
      });
    void pending.catch(error => entered.reject(error));
    try {
      await entered.promise;
      // The actual background editor/map effect installs this same logical target after its independent response.
      state.readOwner.selectTarget(configurationReadTarget(state.selection));
      await state.readOwner.request(async () => ({ state: "ready", context, value: { independentlyRead: true, environment: "Development" } }));
      assert.equal(signal.aborted, false); assert.equal(state.receipts.recoveryBusy, true);
      assert.equal((await recoverConfigurationOriginal(state.readOwner, state.commandOwner, state.receipts, state.receipt, state.selection, true,
        async () => { throw new Error("Concurrent lookup must not enter."); })).reason, "busy");
      assert.equal(signal.aborted, false); assert.equal(invoked, 1);
    } finally { held.resolve(); }
    assert.equal((await pending).kind, "settled"); assert.equal(state.receipts.recoveryBusy, false);
    assert.deepEqual(state.commandOwner.view.snapshot.value, { original: state.receipt.key });
  });

  test(`${view} denied background read settles before independent lookup admission without requiring read success`, async () => {
    const state = fixture(view), entered = deferred(), held = deferred();
    state.readOwner.selectTarget(configurationReadTarget(state.selection));
    const reading = state.readOwner.request(async () => { entered.resolve(); await held.promise; return { state: "denied", context }; });
    let lookups = 0;
    try {
      await entered.promise;
      const refused = await recoverConfigurationOriginal(state.readOwner, state.commandOwner, state.receipts, state.receipt, state.selection, false,
        async () => { lookups++; });
      assert.deepEqual(refused, { kind: "blocked", reason: "busy" });
      assert.equal(state.receipts.recoveryBusy, false); assert.equal(lookups, 0);
    } finally { held.resolve(); }
    await reading;
    assert.equal(state.readOwner.view.state, "denied"); assert.equal(state.readOwner.view.canWrite, false);
    const recovered = await recoverConfigurationOriginal(state.readOwner, state.commandOwner, state.receipts, state.receipt, state.selection, true,
      async (_url, original, signal) => {
        lookups++; assert.equal(signal.aborted, false);
        await state.commandOwner.request(async ticket => { assert.equal(ticket.signal.aborted, false); return { state: "ready", context, value: { original: original.key } }; });
      });
    assert.equal(recovered.kind, "settled"); assert.equal(lookups, 1);
    assert.equal(state.readOwner.view.state, "denied"); assert.equal(state.readOwner.view.canWrite, false, "Lookup success never grants read/write authority.");
    assert.deepEqual(state.commandOwner.view.snapshot.value, { original: state.receipt.key });
  });
}

test("a genuinely changed Configuration target still cancels and joins the original callback", async () => {
  const state = fixture("assignment"), entered = deferred(), held = deferred();
  let signal, settled = false;
  const pending = recoverConfigurationOriginal(state.readOwner, state.commandOwner, state.receipts, state.receipt, state.selection, true,
    async (_url, _original, value) => { signal = value; entered.resolve(); await held.promise; });
  void pending.then(() => { settled = true; });
  try {
    await entered.promise;
    state.readOwner.selectTarget(configurationReadTarget({ ...state.selection, schemaVersion: "2" }));
    assert.equal(signal.aborted, true); assert.equal(state.receipts.recoveryBusy, true);
    await new Promise(resolve => setImmediate(resolve)); assert.equal(settled, false);
  } finally { held.resolve(); }
  assert.equal((await pending).kind, "canceled"); assert.equal(state.receipts.recoveryBusy, false);
});

function fixture(view) {
  let raw = null;
  const receipts = new CommandReceiptStore({ context: { operatorId: context.operatorId, serviceAssociation: context.serviceAssociation }, now: () => now,
    createKey: () => "33333333-3333-3333-3333-333333333333", storage: () => ({ getItem: () => raw, setItem: (_key, value) => { raw = value; }, removeItem: () => { raw = null; } }) });
  const prepared = receipts.prepare(view === "application" ? { family: "management.configuration.source.publish", target: { sourceId } }
    : { family: "management.configuration.assignment.replace", target: { applicationId, configurationId: "shared.settings", schemaVersion: "1" } });
  assert.equal(prepared.kind, "prepared");
  return { receipts, receipt: prepared.receipt, readOwner: new ProtectedStateOwner({ context, receipts, now: () => now }),
    commandOwner: new ProtectedStateOwner({ context, receipts, now: () => now }), selection: { applicationId, configurationId: "shared.settings", schemaVersion: "1", view } };
}
function deferred() { let resolve, reject; const promise = new Promise((done, fail) => { resolve = done; reject = fail; }); return { promise, resolve, reject }; }
