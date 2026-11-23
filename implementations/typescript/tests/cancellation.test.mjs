import test from "node:test";
import assert from "node:assert/strict";
import { createFetchTransport, executeRequest, ClientTransportFailure } from "@cislib/client-core";

const encoder = new TextEncoder();
const prepare = () => ({ url: "https://example.test/management/v1/service-info", method: "GET", headers: {}, maximumResponseBytes: 1024 });
const decode = (_response, bytes) => ({ value: JSON.parse(new TextDecoder().decode(bytes)) });
const options = transport => ({ operation: "read", transport, prepare, decode });

test("pre-canceled call does not request credentials or invoke transport", async () => {
  const controller = new AbortController(); controller.abort();
  let reached = false;
  const result = await executeRequest({ ...options({ async send() { reached = true; throw new Error("must not run"); } }), context: { signal: controller.signal }, credentials: async () => { reached = true; return { kind: "none" }; } });
  assert.equal(result.kind, "canceled"); assert.equal(result.attempts, 0); assert.equal(reached, false);
});

test("one deadline aborts credentials and joins their actual completion", async () => {
  const entered = deferred(); const release = deferred(); let settled = false; let sends = 0;
  const call = executeRequest({ ...options({ async send(request) { sends++; return response(request); } }), timeoutMs: 5,
    credentials: async ({ signal }) => { entered.resolve(signal); await release.promise; return { kind: "none" }; } });
  void call.then(() => { settled = true; });
  const signal = await entered.promise; await pause(15);
  assert.equal(signal.aborted, true); assert.equal(settled, false); assert.equal(sends, 0);
  release.resolve(); const result = await call; assert.equal(result.kind, "timeout"); assert.equal(result.attempts, 0);
});

test("caller cancellation reaches pending transport without abandoning it", async () => {
  const controller = new AbortController(); const entered = deferred(); const release = deferred(); let settled = false;
  const call = executeRequest({ ...options({ async send(request) { entered.resolve(request.signal); await release.promise; throw new DOMException("aborted", "AbortError"); } }), context: { signal: controller.signal } });
  void call.then(() => { settled = true; }); const signal = await entered.promise; controller.abort(); await pause(5);
  assert.equal(signal.aborted, true); assert.equal(settled, false);
  release.resolve(); assert.equal((await call).kind, "canceled");
});

test("deadline cancels streamed body and joins source cleanup", async () => {
  const entered = deferred(); const release = deferred(); let settled = false; let disposed = false;
  const call = executeRequest({ ...options({ async send(request) { return {
    ...response(request), body: new ReadableStream({ start() { entered.resolve(); }, async cancel() { await release.promise; } }),
    async dispose() { disposed = true; }
  }; } }), timeoutMs: 10 });
  void call.then(() => { settled = true; }); await entered.promise; await pause(20);
  assert.equal(settled, false); assert.equal(disposed, false); release.resolve();
  const result = await call; assert.equal(result.kind, "timeout"); assert.equal(disposed, true);
});

test("decoded result survives deadline while actual response disposal drains", async () => {
  const entered = deferred(); const release = deferred(); let settled = false;
  const call = executeRequest({ ...options({ async send(request) { return { ...response(request), async dispose() { entered.resolve(request.signal); await release.promise; } }; } }), timeoutMs: 10 });
  void call.then(() => { settled = true; }); const signal = await entered.promise; await pause(20);
  assert.equal(signal.aborted, true); assert.equal(settled, false); release.resolve();
  const result = await call; assert.equal(result.kind, "http"); assert.deepEqual(result.value, { value: "retained" }); assert.equal(result.cleanupSucceeded, true);
});

test("cancellation joins retry wait and prevents another read", async () => {
  const controller = new AbortController(); let sends = 0;
  const call = executeRequest({ ...options({ async send(request) { sends++; return response(request, 503, { "retry-after": "1" }); } }), readRetries: 2, context: { signal: controller.signal } });
  await pause(10); controller.abort(); const result = await call;
  assert.equal(result.kind, "canceled"); assert.equal(sends, 1);
});

test("unexpected programming fault is preserved even after deadline", async () => {
  const entered = deferred(); const release = deferred(); const original = new Error("programming fault sentinel");
  const call = executeRequest({ ...options({ async send(request) { entered.resolve(request.signal); await release.promise; throw original; } }), timeoutMs: 5 });
  const observed = call.catch(error => error); const signal = await entered.promise; await pause(15); assert.equal(signal.aborted, true);
  release.resolve(); assert.equal(await observed, original);
});

test("mutation cancellation retains original key and uncertainty without retry", async () => {
  let count = 0;
  const result = await executeRequest({ ...options({ async send(request) { count++; await new Promise(resolve => request.signal.addEventListener("abort", resolve, { once: true })); throw new DOMException("aborted", "AbortError"); } }),
    prepare: () => ({ ...prepare(), method: "PUT", body: encoder.encode("{}") }), timeoutMs: 5, readRetries: 2, operationKey: "one-original-key" });
  assert.equal(result.kind, "timeout"); assert.equal(result.operationKey, "one-original-key"); assert.equal(result.mutationUncertain, true); assert.equal(count, 1);
});

test("arbitration cleanup failure and original programming failure remain distinct", async () => {
  const original = new Error("decode sentinel"); const cleanup = new Error("cleanup sentinel");
  const call = executeRequest({ ...options({ async send(request) { return { ...response(request), async dispose() { throw cleanup; } }; } }), decode() { throw original; } });
  await assert.rejects(call, error => error instanceof AggregateError && error.errors[0] === original && error.errors[1] === cleanup);
});

test("injected fetch body read programming faults remain visible including TypeError", async () => {
  for (const original of [new Error("body programming sentinel"), new TypeError("body type sentinel")]) {
    const transport = createFetchTransport(async () => new Response(new ReadableStream({
      pull(controller) { controller.error(original); }
    })));
    await assert.rejects(executeRequest(options(transport)), error => error instanceof AggregateError
      && error.errors[0] === original && error.errors[1] === original);
  }
});

test("injected fetch cleanup programming faults retain both original fault facts", async () => {
  for (const original of [new Error("cancel programming sentinel"), new TypeError("cancel type sentinel")]) {
    const entered = deferred(); const release = deferred(); const controller = new AbortController();
    const transport = createFetchTransport(async () => new Response(new ReadableStream({
      pull() { entered.resolve(); },
      async cancel() { await release.promise; throw original; }
    }, { highWaterMark: 0 })));
    const call = executeRequest({ ...options(transport), context: { signal: controller.signal } });
    const observed = call.catch(error => error);
    await entered.promise; controller.abort();
    release.resolve();
    const failure = await observed;
    assert.ok(failure instanceof AggregateError);
    assert.equal(failure.errors[0], original); assert.equal(failure.errors[1], original);
  }
});

test("injected fetch native-marked body failure remains structured after rejected cleanup", async () => {
  const native = new TypeError("terminated", { cause: Object.assign(new Error("native socket loss"), { code: "UND_ERR_SOCKET" }) });
  const transport = createFetchTransport(async () => new Response(new ReadableStream({
    pull(controller) { controller.error(native); }
  })));
  const result = await executeRequest({ ...options(transport), prepare: () => ({ ...prepare(), method: "PUT", body: encoder.encode("{}") }),
    operationKey: "original-fetch-key", readRetries: 2 });
  assert.equal(result.kind, "transportFailure"); assert.equal(result.status, 200);
  assert.equal(result.cleanupSucceeded, false); assert.equal(result.attempts, 1);
  assert.equal(result.operationKey, "original-fetch-key"); assert.equal(result.mutationUncertain, true);
});

test("fetch cancellation joins native cleanup and retains expected cleanup rejection", async () => {
  const entered = deferred(); const cleanupEntered = deferred(); const release = deferred(); const controller = new AbortController();
  let settled = false;
  const transport = createFetchTransport(async () => new Response(new ReadableStream({
    pull() { entered.resolve(); },
    async cancel() { cleanupEntered.resolve(); await release.promise; throw new DOMException("native abort", "AbortError"); }
  })));
  const call = executeRequest({ ...options(transport), context: { signal: controller.signal } });
  void call.then(() => { settled = true; });
  await entered.promise; controller.abort(); await cleanupEntered.promise;
  assert.equal(settled, false); release.resolve();
  const result = await call;
  assert.equal(result.kind, "canceled"); assert.equal(result.cleanupSucceeded, false); assert.equal(result.attempts, 1);
});

function response(request, status = 200, headers = {}) {
  return { status, url: request.url, redirected: false, headers, body: new ReadableStream({ start(controller) { controller.enqueue(encoder.encode('{"value":"retained"}')); controller.close(); } }), async dispose() {} };
}
function deferred() { let resolve; const promise = new Promise(complete => { resolve = complete; }); return { promise, resolve }; }
function pause(ms) { return new Promise(resolve => setTimeout(resolve, ms)); }
