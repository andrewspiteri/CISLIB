import test from "node:test";
import assert from "node:assert/strict";
import { CommandReceiptStore } from "../src/command-receipts.ts";
import { PortalSessionCoordinator } from "../src/portal-session.ts";
import { ProtectedStateOwner } from "../src/protected-state.ts";
import { createPortalSessionInvalidation, matchesPortalSessionInvalidation, portalSessionInvalidationChannel, portalSessionInvalidationMessage, publishPortalSessionInvalidation,
  readPortalSessionCheck } from "../src/portal-session-check.ts";
import { portalContextHeader, portalContextKey } from "../src/portal-context.ts";

const applicationId = "11111111-1111-4111-8111-111111111111";
function context(session = "2", operator = "1") {
  return { operatorId: operator.repeat(64), sessionAssociation: session.repeat(64), serviceAssociation: "a".repeat(64), expiresAtMs: 10000 };
}
async function fixture(initial = context(), now = () => 1000) {
  let raw = null;
  const storage = { getItem: () => raw, setItem: (_, value) => { raw = value; }, removeItem: () => { raw = null; } };
  const receipts = new CommandReceiptStore({ context: undefined, storage: () => storage, now,
    createKey: () => "22222222-2222-4222-8222-222222222222" });
  const portal = new PortalSessionCoordinator(receipts, now);
  const owner = new ProtectedStateOwner({ context: undefined, receipts, now });
  portal.register(owner);
  owner.selectTarget({ requestKey: "/api/portal/applications" });
  await owner.request(async () => {
    portal.observe(initial, 0, owner);
    return { state: "ready", context: initial, value: "private native observation" };
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

test("an older tab expiry or changed-session check cannot erase a newer valid session's draft and originals", async () => {
  for (const cause of ["expiry", "changed"]) {
    let now = 1000;
    const oldContext = context(), newerContext = { ...context("3"), expiresAtMs: 20000 };
    const old = await fixture(oldContext, () => now), peer = await fixture(oldContext, () => now), newer = await fixture(newerContext, () => now);
    const original = newer.raw(), epoch = newer.portal.getSnapshot().epoch;
    const stop = old.portal.subscribe(() => {
      const value = old.portal.getSnapshot().invalidatedSession;
      peer.portal.receiveSessionInvalidation(value);
      newer.portal.receiveSessionInvalidation(value);
    });
    if (cause === "expiry") { now = 10000; old.portal.checkExpiry(); }
    else { await old.portal.revalidateSession(async () => ({ state: "changed" })); }
    stop();
    assert.equal(old.portal.getSnapshot().invalidatedSession, createPortalSessionInvalidation(oldContext));
    for (const invalid of [old, peer]) {
      assert.equal(invalid.portal.getSnapshot().blocked, true);
      assert.equal(invalid.owner.view.snapshot, undefined); assert.equal(invalid.owner.view.draft, undefined); assert.equal(invalid.raw(), null);
    }
    assert.deepEqual(newer.portal.getSnapshot().context, newerContext);
    assert.equal(newer.portal.getSnapshot().epoch, epoch); assert.equal(newer.portal.getSnapshot().blocked, false);
    assert.equal(newer.owner.view.draft.text, "unsaved JSON"); assert.equal(newer.owner.view.snapshot.value, "private native observation");
    assert.equal(newer.raw(), original); assert.equal(newer.receipts.load().receipts.length, 1);
  }
});

test("matching old-session signout clears peers immediately without abandoning held work or another session's originals", async () => {
  const source = await fixture(), peer = await fixture(), newer = await fixture(context("3"));
  const original = newer.raw(), held = Promise.withResolvers(), entered = Promise.withResolvers();
  let signal, settled = false;
  const pending = peer.portal.revalidateSession(async (_, value) => {
    signal = value; entered.resolve(); await held.promise; return { state: "current", context: context() };
  });
  pending.then(() => { settled = true; });
  await entered.promise;
  const stop = source.portal.subscribe(() => {
    const value = source.portal.getSnapshot().invalidatedSession;
    peer.portal.receiveSessionInvalidation(value); newer.portal.receiveSessionInvalidation(value);
  });
  source.portal.signOut(); stop();
  assert.equal(signal.aborted, true); assert.equal(settled, false);
  assert.equal(peer.owner.view.snapshot, undefined); assert.equal(peer.owner.view.draft, undefined); assert.equal(peer.raw(), null);
  const epoch = peer.portal.getSnapshot().epoch;
  assert.equal(peer.portal.receiveSessionInvalidation(source.portal.getSnapshot().invalidatedSession), false);
  assert.equal(peer.portal.getSnapshot().epoch, epoch);
  assert.equal(newer.owner.view.draft.text, "unsaved JSON"); assert.equal(newer.raw(), original);
  held.resolve(); await pending;
  assert.equal(peer.portal.getSnapshot().blocked, true); assert.equal(peer.portal.getSnapshot().context, undefined);
});

test("legacy, malformed and different session/service invalidations cannot clear protected state or originals", async () => {
  const f = await fixture(), original = f.raw(), actual = createPortalSessionInvalidation(context());
  const values = [undefined, null, portalSessionInvalidationMessage, {}, [actual], actual + ".extra", actual + "\n", actual.toUpperCase(),
    createPortalSessionInvalidation(context("3")), createPortalSessionInvalidation({ ...context(), serviceAssociation: "b".repeat(64) })];
  assert.equal(matchesPortalSessionInvalidation(actual, undefined), false);
  for (const value of values) {
    assert.equal(f.portal.receiveSessionInvalidation(value), false);
    assert.equal(f.owner.view.draft.text, "unsaved JSON"); assert.equal(f.raw(), original);
  }
  assert.equal(f.portal.receiveSessionInvalidation(actual), true);
  assert.equal(f.owner.view.draft, undefined); assert.equal(f.raw(), null);
});

test("peer invalidation carries only closed nonsecret session/service associations with independent transport fallback", () => {
  const sent = [], stored = [];
  const current = context(), value = createPortalSessionInvalidation(current);
  assert.equal(value, `invalidate:${current.sessionAssociation}.${current.serviceAssociation}`);
  assert.equal(value.includes(current.operatorId), false);
  const storage = { setItem: (key, value) => stored.push([key, value]), removeItem: key => stored.push([key]) };
  publishPortalSessionInvalidation(value, { postMessage: message => sent.push(message) }, () => storage);
  assert.deepEqual(sent, [value]);
  assert.deepEqual(stored, [[portalSessionInvalidationChannel, value], [portalSessionInvalidationChannel]]);
  stored.length = 0;
  publishPortalSessionInvalidation(value, { postMessage: () => { throw new Error("channel unavailable"); } }, () => storage);
  assert.deepEqual(stored, [[portalSessionInvalidationChannel, value], [portalSessionInvalidationChannel]]);
  assert.doesNotThrow(() => publishPortalSessionInvalidation(value, undefined, () => { throw new Error("storage unavailable"); }));
  sent.length = 0; stored.length = 0;
  for (const invalid of [undefined, null, portalSessionInvalidationMessage, value + ".extra", value + "\n", value.toUpperCase()]) {
    publishPortalSessionInvalidation(invalid, { postMessage: message => sent.push(message) }, () => storage);
  }
  assert.deepEqual(sent, []); assert.deepEqual(stored, []);
});
