import test from "node:test";
import assert from "node:assert/strict";
import { decodeManagement } from "@cislib/management-client";
import { PortalRequestInvalid, readPortalJson } from "../src/portal-body.ts";

const encoder = new TextEncoder();
function request(body, headers = { "content-type": "application/json" }) {
  return new Request("https://portal.test/api/portal/applications/11111111-1111-4111-8111-111111111111/metadata",
    { method: "PUT", headers, body, ...(typeof body === "string" ? {} : { duplex: "half" }) });
}

test("portal body preserves exact text for the native command decoder", async () => {
  const raw = ' { "commandVersion":1,"displayName":"Café 日本語","description":"" } ';
  assert.equal(await readPortalJson(request(raw), new AbortController().signal), raw);
  for (const version of ["1.0", "1e0"]) {
    const text = await readPortalJson(request(`{"commandVersion":${version},"displayName":"Name"}`), new AbortController().signal);
    assert.throws(() => decodeManagement("MetadataCommand", text, "request"));
  }
});

test("invalid UTF8 duplicated fields nonobjects and malformed JSON never become normalized commands", async () => {
  for (const text of ['{"displayName":"one","displayName":"two"}', "[]", "null", '{"displayName":', '{"displayName":"\\ud800"}', '\ufeff{"commandVersion":1,"displayName":"Name"}']) {
    await assert.rejects(readPortalJson(request(text), new AbortController().signal), PortalRequestInvalid);
  }
  const bad = new ReadableStream({ start(controller) { controller.enqueue(new Uint8Array([0xc0, 0xaf])); controller.close(); } });
  await assert.rejects(readPortalJson(request(bad), new AbortController().signal), PortalRequestInvalid);
});

test("an over-limit streamed body cancels and joins its underlying owner", async () => {
  let entered; let release; let returned = false;
  const canceled = new Promise(resolve => { entered = resolve; });
  const held = new Promise(resolve => { release = resolve; });
  const body = new ReadableStream({ start(controller) { controller.enqueue(encoder.encode('{"value":"too large"}')); },
    async cancel() { entered(); await held; } });
  const pending = readPortalJson(request(body), new AbortController().signal, 4);
  void pending.then(() => { returned = true; }, () => { returned = true; });
  await canceled; await new Promise(resolve => setImmediate(resolve)); assert.equal(returned, false);
  release(); await assert.rejects(pending, error => error instanceof PortalRequestInvalid && error.status === 413);
});

test("cancellation retains the pending body until actual cancellation cleanup settles", async () => {
  let entered; let release; let returned = false;
  const canceled = new Promise(resolve => { entered = resolve; });
  const held = new Promise(resolve => { release = resolve; });
  const body = new ReadableStream({ async cancel() { entered(); await held; } });
  const controller = new AbortController();
  const pending = readPortalJson(request(body), controller.signal);
  void pending.then(() => { returned = true; }, () => { returned = true; });
  controller.abort(); await canceled; await new Promise(resolve => setImmediate(resolve)); assert.equal(returned, false);
  release(); await assert.rejects(pending, error => error.name === "AbortError");
});

test("rejected content type and length join body cleanup before reporting invalid input", async () => {
  for (const headers of [{ "content-type": "text/plain" }, { "content-type": "application/json", "content-length": "70000" }]) {
    let entered; let release; let returned = false;
    const canceled = new Promise(resolve => { entered = resolve; }); const held = new Promise(resolve => { release = resolve; });
    const body = new ReadableStream({ async cancel() { entered(); await held; } });
    const pending = readPortalJson(request(body, headers), new AbortController().signal);
    void pending.then(() => { returned = true; }, () => { returned = true; });
    await canceled; await new Promise(resolve => setImmediate(resolve)); assert.equal(returned, false);
    release(); await assert.rejects(pending, PortalRequestInvalid);
  }
});

test("unexpected underlying cleanup faults remain identifiable with the original body refusal", async () => {
  const fault = Error("controlled cleanup fault");
  const body = new ReadableStream({ start(controller) { controller.enqueue(encoder.encode('{"long":true}')); }, cancel() { throw fault; } });
  await assert.rejects(readPortalJson(request(body), new AbortController().signal, 2), error =>
    error instanceof AggregateError && error.errors[0] instanceof PortalRequestInvalid && error.errors[1] === fault);
});
