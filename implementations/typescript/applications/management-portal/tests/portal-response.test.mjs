import assert from "node:assert/strict";
import test from "node:test";
import { handoffPortalResponse } from "../src/portal-response.ts";

test("an already canceled request admits no callback and returns no protected body or context", async () => {
  const controller = new AbortController(); controller.abort();
  let admitted = 0;
  const response = await handoffPortalResponse(controller.signal, async () => { admitted++; return Response.json({ secret: "protected" }); });
  assert.equal(admitted, 0);
  assert.equal(response.status, 499); assert.equal(response.body, null);
  assert.equal(await response.text(), "");
  assert.equal(response.headers.get("Cache-Control"), "private, no-store, max-age=0");
  assert.equal(response.headers.get("Pragma"), "no-cache"); assert.equal(response.headers.get("Vary"), "Cookie");
  assert.equal(response.headers.get("Content-Type"), null);
});

test("a delayed admitted callback remains owned through abort and its real cleanup before bodyless handoff", async () => {
  const controller = new AbortController(), entered = deferred(), completed = deferred(), cleanup = deferred();
  const order = [];
  const pending = handoffPortalResponse(controller.signal, async () => {
    entered.resolve();
    try { await completed.promise; return Response.json({ state: "ready", value: "protected", context: "private-context" }); }
    finally { order.push("cleanup-entered"); await cleanup.promise; order.push("cleanup-complete"); }
  });
  let settled = false; void pending.then(() => { settled = true; }, () => { settled = true; });
  await entered.promise; controller.abort();
  await Promise.resolve(); assert.equal(settled, false);
  completed.resolve(); await Promise.resolve(); assert.equal(settled, false);
  cleanup.resolve();
  const response = await pending;
  assert.deepEqual(order, ["cleanup-entered", "cleanup-complete"]);
  assert.equal(response.status, 499); assert.equal(response.body, null);
});

test("an aborted outgoing response body is canceled and its actual asynchronous cancellation is joined", async () => {
  const controller = new AbortController(), cancelEntered = deferred(), cancelComplete = deferred();
  let cancellations = 0;
  const stream = new ReadableStream({
    start(value) { value.enqueue(new TextEncoder().encode('{"state":"ready","value":"protected"}')); },
    async cancel() { cancellations++; cancelEntered.resolve(); await cancelComplete.promise; }
  });
  const original = new Response(stream, { headers: { "Content-Type": "application/json", "X-Private-Context": "hidden" } });
  const pending = handoffPortalResponse(controller.signal, async () => { controller.abort(); return original; });
  let settled = false; void pending.then(() => { settled = true; }, () => { settled = true; });
  await cancelEntered.promise;
  assert.equal(cancellations, 1); assert.equal(settled, false);
  cancelComplete.resolve();
  const response = await pending;
  assert.equal(response.status, 499); assert.equal(response.body, null);
  assert.equal(response.headers.get("X-Private-Context"), null);
  assert.equal(original.bodyUsed, true);
});

test("body cancellation failure is preserved rather than reported as a clean canceled response", async () => {
  const controller = new AbortController(), failure = new Error("Owned body cancellation did not settle cleanly");
  const original = new Response(new ReadableStream({ cancel() { throw failure; } }));
  await assert.rejects(handoffPortalResponse(controller.signal, async () => { controller.abort(); return original; }), error => error === failure);
});

test("an admitted failure retains its original identity and awaited cleanup after cancellation", async () => {
  const controller = new AbortController(), entered = deferred(), cleanup = deferred();
  const original = new Error("Original native callback failed");
  let cleaned = false;
  const pending = handoffPortalResponse(controller.signal, async () => {
    try { throw original; }
    finally { entered.resolve(); await cleanup.promise; cleaned = true; }
  });
  const rejected = assert.rejects(pending, error => error === original);
  await entered.promise; controller.abort(); assert.equal(cleaned, false);
  cleanup.resolve(); await rejected; assert.equal(cleaned, true);
});

test("a noncanceled handoff preserves the original response identity status headers and exact JSON bytes", async () => {
  const controller = new AbortController();
  const raw = String.raw`{"large":9007199254740993,"unicode":"\u65e5 日本語","false":false,"zero":-0,"null":null}`;
  const original = new Response(raw, { status: 409, headers: { "Content-Type": "application/json", "X-Original": "retained" } });
  const response = await handoffPortalResponse(controller.signal, async () => original);
  assert.equal(response, original); assert.equal(response.status, 409);
  assert.equal(response.headers.get("X-Original"), "retained"); assert.equal(await response.text(), raw);
});

test("cancellation after completed handoff does not claim to cancel the framework's subsequent response pipeline", async () => {
  const controller = new AbortController();
  const original = Response.json({ state: "ready", value: "already handed off" });
  const response = await handoffPortalResponse(controller.signal, async () => original);
  controller.abort();
  assert.equal(response, original); assert.equal(response.status, 200);
  assert.deepEqual(await response.json(), { state: "ready", value: "already handed off" });
});

function deferred() {
  let resolve;
  const promise = new Promise(done => { resolve = done; });
  return { promise, resolve };
}
