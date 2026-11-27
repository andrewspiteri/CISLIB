import assert from "node:assert/strict";
import test from "node:test";
import { CommandReceiptStore } from "../src/command-receipts.ts";
import { ProtectedStateOwner } from "../src/protected-state.ts";
import { recoverMetadataOriginal } from "../src/metadata-recovery.ts";

const applicationId = "11111111-1111-1111-1111-111111111111";
const key = "22222222-2222-2222-2222-222222222222";
const now = 1800000000000;
const context = { operatorId: "a".repeat(64), serviceAssociation: "b".repeat(64), sessionAssociation: "c".repeat(64), expiresAtMs: now + 600000 };

test("exact target selection precedes recovery admission so the original lookup signal stays live", async () => {
  const { owner, receipts, receipt } = fixture();
  const entered = deferred(), held = deferred();
  let signal;
  const pending = recoverMetadataOriginal(owner, receipts, receipt, applicationId, async (url, original, recoverySignal) => {
    signal = recoverySignal;
    // This is also performed by the actual command request; the already selected target must be a no-op.
    owner.selectTarget({ requestKey: url + "#lookup", applicationId });
    assert.equal(signal.aborted, false); assert.equal(original.key, key);
    await owner.request(async ticket => {
      assert.equal(ticket.signal.aborted, false); entered.resolve(); await held.promise;
      return { state: "ready", context, value: { original: original.key } };
    });
  });
  void pending.catch(error => entered.reject(error));
  try {
    await entered.promise;
    assert.equal(signal.aborted, false); assert.equal(receipts.recoveryBusy, true);
    assert.equal(owner.view.pendingRequests, 1);
    assert.equal((await recoverMetadataOriginal(owner, receipts, receipt, applicationId, async () => { throw new Error("must not enter"); })).reason, "busy");
    assert.equal(signal.aborted, false, "A refused concurrent admission must not cancel the retained original.");
  } finally { held.resolve(); }
  assert.equal((await pending).kind, "settled"); assert.equal(receipts.recoveryBusy, false);
  assert.deepEqual(owner.view.snapshot.value, { original: key });
});

test("canceling original lookup retains its selection and owner custody until the actual callback settles", async () => {
  const { owner, receipts, receipt } = fixture();
  const entered = deferred(), held = deferred();
  let signal, settled = false;
  const pending = recoverMetadataOriginal(owner, receipts, receipt, applicationId, async (_url, _original, recoverySignal) => {
    signal = recoverySignal; entered.resolve(); await held.promise;
  });
  void pending.then(() => { settled = true; });
  try {
    await entered.promise; receipts.cancelRecovery();
    assert.equal(signal.aborted, true); assert.equal(receipts.recoveryBusy, true);
    await new Promise(resolve => setImmediate(resolve)); assert.equal(settled, false);
  } finally { held.resolve(); }
  assert.equal((await pending).kind, "canceled"); assert.equal(receipts.recoveryBusy, false);
});

function fixture() {
  let raw = null;
  const receipts = new CommandReceiptStore({ context: { operatorId: context.operatorId, serviceAssociation: context.serviceAssociation },
    now: () => now, createKey: () => key, storage: () => ({ getItem: () => raw, setItem: (_key, value) => { raw = value; }, removeItem: () => { raw = null; } }) });
  const prepared = receipts.prepare({ family: "management.metadata.replace", target: { applicationId } });
  assert.equal(prepared.kind, "prepared");
  const owner = new ProtectedStateOwner({ context, receipts, now: () => now });
  return { owner, receipts, receipt: prepared.receipt };
}
function deferred() { let resolve, reject; const promise = new Promise((done, fail) => { resolve = done; reject = fail; }); return { promise, resolve, reject }; }
