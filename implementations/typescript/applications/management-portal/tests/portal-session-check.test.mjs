import test from "node:test";
import assert from "node:assert/strict";
import { CommandReceiptStore } from "../src/command-receipts.ts";
import { PortalSessionCoordinator } from "../src/portal-session.ts";
import { ProtectedStateOwner } from "../src/protected-state.ts";
import { portalSessionInvalidationChannel, portalSessionInvalidationMessage, publishPortalSessionInvalidation,
  readPortalSessionCheck } from "../src/portal-session-check.ts";
import { portalContextHeader, portalContextKey } from "../src/portal-context.ts";

const applicationId = "11111111-1111-4111-8111-111111111111";
function context(session = "2", operator = "1") {
  return { operatorId: operator.repeat(64), sessionAssociation: session.repeat(64), serviceAssociation: "a".repeat(64), expiresAtMs: 10000 };
}
async function fixture() {
  let raw = null;
  const storage = { getItem: () => raw, setItem: (_, value) => { raw = value; }, removeItem: () => { raw = null; } };
  const receipts = new CommandReceiptStore({ context: undefined, storage: () => storage, now: () => 1000,
    createKey: () => "22222222-2222-4222-8222-222222222222" });
  const portal = new PortalSessionCoordinator(receipts, () => 1000);
  const owner = new ProtectedStateOwner({ context: undefined, receipts, now: () => 1000 });
  portal.register(owner);
  owner.selectTarget({ requestKey: "/api/portal/applications" });
  await owner.request(async () => {
    portal.observe(context(), 0, owner);
    return { state: "ready", context: context(), value: "private native observation" };
  });
  owner.startDraft("baseline JSON"); owner.editDraft("unsaved JSON");
  assert.equal(receipts.prepare({ family: "management.metadata.replace", target: { applicationId } }).kind, "prepared");
  return { portal, owner, receipts, raw: () => raw };
}

test("an active document coalesces one real session check and preserves a same-session draft and originals", async () => {
  const f = await fixture(), held = Promise.withResolvers();
  const original = f.raw();
  let calls = 0;
  const first = f.portal.revalidateSession(async (current, signal) => {
    calls++; assert.equal(signal.aborted, false); assert.deepEqual(current, context());
    await held.promise;
    return { state: "current", context: { ...current, expiresAtMs: 11000 } };
  });
  assert.equal(f.portal.getSnapshot().verification, "checking");
  assert.equal(f.owner.view.draft.text, "unsaved JSON");
  assert.equal(f.portal.revalidateSession(async () => { throw new Error("A second activation must not start another request."); }), first);
  held.resolve(); await first;
  assert.equal(calls, 1); assert.equal(f.portal.getSnapshot().verification, undefined);
  assert.equal(f.portal.getSnapshot().context.expiresAtMs, 11000);
  assert.equal(f.owner.view.draft.text, "unsaved JSON"); assert.equal(f.raw(), original);
});

test("a definite sign-out or changed principal clears every private value, draft and original reference", async () => {
  for (const state of ["unauthenticated", "changed"]) {
    const f = await fixture();
    await f.portal.revalidateSession(async () => ({ state }));
    assert.equal(f.portal.getSnapshot().blocked, true);
    assert.equal(f.portal.getSnapshot().context, undefined);
    assert.equal(f.owner.view.snapshot, undefined); assert.equal(f.owner.view.draft, undefined); assert.equal(f.raw(), null);
    const epoch = f.portal.getSnapshot().epoch;
    f.portal.signOut(); assert.equal(f.portal.getSnapshot().epoch, epoch, "Peer invalidation cannot rebroadcast an unbounded epoch loop.");
  }
});

test("sign-out during a held check invalidates immediately but retains actual callback custody", async () => {
  const f = await fixture(), entered = Promise.withResolvers(), held = Promise.withResolvers();
  let signal, settled = false;
  const pending = f.portal.revalidateSession(async (_, actualSignal) => {
    signal = actualSignal; entered.resolve(); await held.promise;
    return { state: "current", context: context() };
  });
  pending.then(() => { settled = true; });
  await entered.promise;
  f.portal.signOut();
  assert.equal(signal.aborted, true); assert.equal(settled, false);
  assert.equal(f.owner.view.snapshot, undefined); assert.equal(f.owner.view.draft, undefined); assert.equal(f.raw(), null);
  held.resolve(); await pending;
  assert.equal(f.portal.getSnapshot().blocked, true); assert.equal(f.portal.getSnapshot().verification, undefined);
});

test("an unverifiable session stays masked without losing a draft or known originals until explicit successful retry", async () => {
  const f = await fixture(), original = f.raw();
  await f.portal.revalidateSession(async () => { throw new Error("unavailable transport"); });
  assert.equal(f.portal.getSnapshot().verification, "unavailable"); assert.equal(f.portal.getSnapshot().blocked, false);
  assert.equal(f.owner.view.draft.text, "unsaved JSON"); assert.equal(f.raw(), original);
  await f.portal.revalidateSession(async () => ({ state: "current", context: context() }));
  assert.equal(f.portal.getSnapshot().verification, undefined);
  assert.equal(f.owner.view.draft.text, "unsaved JSON"); assert.equal(f.raw(), original);
});

test("the finite session-check deadline masks and aborts without abandoning an unsettled callback", async t => {
  t.mock.timers.enable({ apis: ["setTimeout"] });
  const f = await fixture(), entered = Promise.withResolvers(), held = Promise.withResolvers();
  let signal, settled = false;
  const pending = f.portal.revalidateSession(async (_, value) => {
    signal = value; entered.resolve(); await held.promise;
    return { state: "current", context: context() };
  });
  pending.then(() => { settled = true; });
  await entered.promise; t.mock.timers.tick(15000);
  assert.equal(signal.aborted, true); assert.equal(settled, false);
  assert.equal(f.portal.getSnapshot().verification, "unavailable");
  assert.equal(f.portal.cancelSessionCheck(), pending);
  assert.equal(f.portal.revalidateSession(async () => ({ state: "current", context: context() })), pending);
  held.resolve(); await pending;
  assert.equal(f.portal.getSnapshot().verification, "unavailable", "A timed-out late reply cannot unmask the document.");
  await f.portal.revalidateSession(async () => ({ state: "current", context: context() }));
  assert.equal(f.portal.getSnapshot().verification, undefined);
});

test("the existing BFF context check sends only an opaque context header and keeps no application values", async () => {
  const current = context();
  let captured;
  const result = await readPortalSessionCheck(current, new AbortController().signal, async (url, init) => {
    captured = { url, init };
    return Response.json({ state: "ready", context: current, value: { items: [{ private: "not retained by the check" }] } });
  });
  assert.equal(captured.url, "/api/portal/applications");
  assert.equal(captured.init.credentials, "same-origin"); assert.equal(captured.init.cache, "no-store"); assert.equal(captured.init.redirect, "error");
  assert.deepEqual(captured.init.headers, { [portalContextHeader]: portalContextKey(current) });
  assert.deepEqual(result, { state: "current", context: current });
});

test("only an actual matching BFF response can unmask; unauthorized and changed contexts stay distinct from failures", async () => {
  const cases = [
    [401, { state: "unauthenticated" }, "unauthenticated"],
    [409, { state: "invalid", context: context("3", "2") }, "changed"],
    [200, { state: "ready", context: context("3") }, "changed"],
    [503, { state: "unavailable", context: context() }, "unavailable"],
    [409, { state: "invalid", context: context() }, "unavailable"],
    [200, { state: "ready", context: { ...context(), access_token: "never accepted" } }, "unavailable"],
    [401, { state: "ready" }, "unavailable"]
  ];
  for (const [status, body, expected] of cases) {
    const result = await readPortalSessionCheck(context(), new AbortController().signal, async () => Response.json(body, { status }));
    assert.equal(result.state, expected);
  }
});

test("peer invalidation carries one fixed credential-free message and degrades to the independent available transport", () => {
  const sent = [], stored = [];
  const storage = { setItem: (key, value) => stored.push([key, value]), removeItem: key => stored.push([key]) };
  publishPortalSessionInvalidation({ postMessage: message => sent.push(message) }, () => storage);
  assert.deepEqual(sent, [portalSessionInvalidationMessage]);
  assert.deepEqual(stored, [[portalSessionInvalidationChannel, portalSessionInvalidationMessage], [portalSessionInvalidationChannel]]);
  stored.length = 0;
  publishPortalSessionInvalidation({ postMessage: () => { throw new Error("channel unavailable"); } }, () => storage);
  assert.deepEqual(stored, [[portalSessionInvalidationChannel, portalSessionInvalidationMessage], [portalSessionInvalidationChannel]]);
  assert.doesNotThrow(() => publishPortalSessionInvalidation(undefined, () => { throw new Error("storage unavailable"); }));
});
