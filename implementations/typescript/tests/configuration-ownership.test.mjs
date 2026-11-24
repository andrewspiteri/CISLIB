import test from "node:test";
import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import { ManagementConfigurationClient } from "@cislib/management-client";
import { executeRequest, ClientProtocolFailure, ClientTransportFailure } from "@cislib/client-core";

const catalog = JSON.parse(await readFile(new URL("../../../conformance/management/v1/configuration-cases.json", import.meta.url), "utf8"));
const snapshot = JSON.parse(catalog.cases.find(row => row.id === "configuration-snapshot-valid").json);
const { releaseId: _release, generation: _generation, ...target } = snapshot.desired;

test("deadline joins delayed digest before cleanup and cannot create a late identified rejection", async () => {
  for (const mismatch of [false, true]) {
    const entered = deferred(); const release = deferred(); let disposed = 0; let settled = false; let signal; let call;
    const native = globalThis.crypto;
    const restore = cryptoWith(async (...arguments_) => {
      entered.resolve(); await release.promise;
      return mismatch ? new Uint8Array(32).buffer : native.subtle.digest(...arguments_);
    });
    try {
      const client = new ManagementConfigurationClient({ baseUrl: "https://example.test/", timeoutMs: 50, transport: { async send(request) {
        signal = request.signal; return { ...response(request), async dispose() { disposed++; } };
      } } });
      call = client.getRegistrationConfiguration(target); void call.then(() => { settled = true; }, () => { settled = true; });
      await entered.promise; await pause(80);
      assert.equal(signal.aborted, true); assert.equal(settled, false); assert.equal(disposed, 0);
      release.resolve(); const result = await call;
      assert.equal(result.kind, "timeout"); assert.equal(result.status, 200); assert.equal(disposed, 1);
      assert.equal(result.contentRejection, undefined); assert.equal(result.value, undefined);
    } finally { release.resolve(); if (call !== undefined) { await call.catch(() => undefined); } restore(); }
  }
});

test("unexpected digest faults remain original after owned cancellation and actual response cleanup", async () => {
  for (const original of [new Error("native digest sentinel"), new TypeError("digest type sentinel")]) {
    const entered = deferred(); const release = deferred(); let disposed = false; let observed;
    const restore = cryptoWith(async () => { entered.resolve(); await release.promise; throw original; });
    try {
      const client = new ManagementConfigurationClient({ baseUrl: "https://example.test/", timeoutMs: 50, transport: { async send(request) {
        return { ...response(request), async dispose() { disposed = true; } };
      } } });
      observed = client.getRegistrationConfiguration(target).catch(error => error);
      await entered.promise; await pause(80); assert.equal(disposed, false); release.resolve();
      assert.equal(await observed, original); assert.equal(disposed, true);
    } finally { release.resolve(); if (observed !== undefined) { await observed; } restore(); }
  }
});

test("asynchronous decode and original unexpected cleanup fault retain both identities", async () => {
  const original = new Error("digest sentinel"); const cleanup = new Error("cleanup sentinel");
  const restore = cryptoWith(async () => { throw original; });
  try {
    const client = new ManagementConfigurationClient({ baseUrl: "https://example.test/", transport: { async send(request) {
      return { ...response(request), async dispose() { throw cleanup; } };
    } } });
    await assert.rejects(client.getRegistrationConfiguration(target), error => error instanceof AggregateError
      && error.errors[0] === original && error.errors[1] === cleanup);
  } finally { restore(); }
});

test("legacy mutation classification remains default and explicit read-only POST never retries", async () => {
  for (const [method, mutating, expected] of [["GET", undefined, false], ["PUT", undefined, true], ["DELETE", undefined, true],
    ["POST", undefined, true], ["POST", false, false]]) {
    let sends = 0;
    const result = await executeRequest({ operation: "compatibility", readRetries: 2, operationKey: "original-key",
      prepare: () => ({ url: "https://example.test/api", method, headers: {}, maximumResponseBytes: 1024,
        ...(mutating === undefined ? {} : { mutating }) }),
      transport: { async send() { sends++; throw new ClientTransportFailure("transport.unavailable"); } },
      decode: () => ({ value: "unused" }) });
    assert.equal(result.mutationUncertain, expected); assert.equal(result.operationKey, "original-key"); assert.equal(sends, 1);
  }
});

test("asynchronous preparation is within the entry deadline and joins its actual work before dispatch", async () => {
  const entered = deferred(); const release = deferred(); let sent = 0; let settled = false;
  const call = executeRequest({ operation: "async-prepare", timeoutMs: 50,
    prepare: async signal => { entered.resolve(signal); await release.promise; return { url: "https://example.test/api", method: "GET", headers: {}, maximumResponseBytes: 1024 }; },
    transport: { async send(request) { sent++; return response(request); } }, decode: () => ({ value: "unused" }) });
  void call.then(() => { settled = true; }, () => { settled = true; });
  try {
    const signal = await entered.promise; await pause(80);
    assert.equal(signal.aborted, true); assert.equal(settled, false); assert.equal(sent, 0);
  } finally { release.resolve(); await call.catch(() => undefined); }
  assert.equal((await call).kind, "timeout"); assert.equal(sent, 0);
});

test("conditional permission is narrow GET-only and expected late decode rejection observes owned cancellation", async () => {
  await assert.rejects(executeRequest({ operation: "bad-304", prepare: () => ({ url: "https://example.test/api", method: "POST",
    headers: { "If-None-Match": '"token"' }, maximumResponseBytes: 1024, allowNotModified: true }),
    transport: { async send() { throw new Error("must not dispatch"); } }, decode: () => ({}) }), TypeError);
  const entered = deferred(); const release = deferred();
  const call = executeRequest({ operation: "late-decode", timeoutMs: 50,
    prepare: () => ({ url: "https://example.test/api", method: "GET", headers: {}, maximumResponseBytes: 1048576 }),
    transport: { async send(request) { return response(request); } },
    decode: async () => { entered.resolve(); await release.promise; throw new ClientProtocolFailure("expected.late"); } });
  try { await entered.promise; await pause(80); }
  finally { release.resolve(); await call.catch(() => undefined); }
  assert.equal((await call).kind, "timeout");
});

function cryptoWith(digest) {
  const descriptor = Object.getOwnPropertyDescriptor(globalThis, "crypto");
  Object.defineProperty(globalThis, "crypto", { configurable: true, value: { subtle: { digest } } });
  return () => { if (descriptor === undefined) { delete globalThis.crypto; } else { Object.defineProperty(globalThis, "crypto", descriptor); } };
}
function response(request) {
  return { status: 200, url: request.url, redirected: false, headers: { "content-type": "application/json" },
    body: new ReadableStream({ start(controller) { controller.enqueue(new TextEncoder().encode(JSON.stringify(snapshot))); controller.close(); } }), async dispose() {} };
}
function deferred() { let resolve; const promise = new Promise(complete => { resolve = complete; }); return { promise, resolve }; }
function pause(milliseconds) { return new Promise(resolve => setTimeout(resolve, milliseconds)); }
