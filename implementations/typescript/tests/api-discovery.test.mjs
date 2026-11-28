import test from "node:test";
import assert from "node:assert/strict";
import { ManagementClient, decodeManagement, ManagementWireError } from "@cislib/management-client";

const app = "11111111-1111-1111-1111-111111111111";
const otherApp = "22222222-2222-2222-2222-222222222222";
const feature = index => ({ operationId: "operation" + String(index).padStart(3, "0"), version: 1, method: "GET",
  path: "/management/v1/example/" + index, requiredCapabilities: ["management.applications.read"] });

test("complete discovery pages more than the legacy ceiling without a hidden service-info request", async () => {
  const records = [], inventory = Array.from({ length: 70 }, (_, index) => feature(index));
  const client = new ManagementClient({ baseUrl: "https://example.test/prefix", transport: { async send(request) {
    records.push(request); const query = new URL(request.url).searchParams;
    const start = query.has("cursor") ? Number(query.get("cursor")) : 0;
    const end = Math.min(start + 32, inventory.length);
    return response(request, { profile: "complete-v1", items: inventory.slice(start, end), ...(end < inventory.length ? { nextCursor: String(end) } : {}) });
  } } });
  const observed = []; let cursor;
  do {
    const result = await client.listServiceFeatures(cursor === undefined ? undefined : { cursor });
    assert.equal(result.kind, "http"); observed.push(...result.value.items); cursor = result.value.nextCursor;
  } while (cursor !== undefined);
  assert.deepEqual(observed, inventory); assert.equal(records.length, 3);
  assert.ok(records.every(row => new URL(row.url).pathname === "/prefix/management/v1/service-features" && new URL(row.url).searchParams.get("pageSize") === "32"));
  assert.ok(records.every(row => row.method === "GET" && row.headers["Idempotency-Key"] === undefined));
});

test("application discovery retains an exact application echo and advisory hints with deeply frozen features", async () => {
  const requests = []; let disposed = 0;
  const page = { applicationId: app, profile: "complete-v1", items: [{ feature: feature(1), allowed: false }], nextCursor: "opaque+/?" };
  const client = new ManagementClient({ baseUrl: "https://example.test/prefix", transport: { async send(request) {
    requests.push(request); return response(request, page, async () => { disposed++; });
  } } });
  const result = await client.listApplicationApiCapabilities(app, { pageSize: 64, cursor: "opaque+/?" });
  assert.equal(result.kind, "http"); assert.deepEqual(result.value, page); assert.equal(disposed, 1);
  assert.equal(requests[0].url, `https://example.test/prefix/management/v1/applications/${app}/api-capabilities?pageSize=64&cursor=opaque%2B%2F%3F`);
  assert.ok(Object.isFrozen(result.value.items[0].feature.requiredCapabilities));
  assert.throws(() => result.value.items[0].feature.requiredCapabilities.push("invented"), TypeError);
});

test("discovery rejects mismatched profile and application echoes and joins response cleanup", async () => {
  for (const body of [
    { applicationId: otherApp, profile: "complete-v1", items: [] },
    { applicationId: app, profile: "future-v2", items: [] }
  ]) {
    let disposed = 0;
    const client = new ManagementClient({ baseUrl: "https://example.test/", transport: { async send(request) {
      return response(request, body, async () => { disposed++; });
    } } });
    const result = await client.listApplicationApiCapabilities(app);
    assert.equal(result.kind, "protocolFailure"); assert.equal(result.value, undefined); assert.equal(disposed, 1);
  }
  const client = new ManagementClient({ baseUrl: "https://example.test/", transport: { async send(request) { return response(request, { profile: "legacy-v1", items: [] }); } } });
  assert.equal((await client.listServiceFeatures()).kind, "protocolFailure");
});

test("discovery uses its own 1 through 64 request limit and rejects invalid cursors before transport", async () => {
  let sends = 0;
  const client = new ManagementClient({ baseUrl: "https://example.test/", transport: { async send() { sends++; throw new Error("not reached"); } } });
  for (const page of [{ pageSize: 0 }, { pageSize: 65 }, { pageSize: 1.5 }, { cursor: "not opaque" }]) {
    await assert.rejects(client.listServiceFeatures(page), TypeError);
    await assert.rejects(client.listApplicationApiCapabilities(app, page), TypeError);
  }
  assert.equal(sends, 0);
});

test("legacy discovery remains readable with absent or future advertisements and retains its 64 item boundary", () => {
  for (const advertisement of [undefined, 1, 2]) {
    const info = { serviceVersion: "1", protocolMajors: [1], features: [], ...(advertisement === undefined ? {} : { apiDiscoveryVersion: advertisement }) };
    assert.deepEqual(decodeManagement("ServiceInfo", JSON.stringify(info), "response"), info);
  }
  const base = { serviceVersion: "1", protocolMajors: [1] };
  assert.equal(decodeManagement("ServiceInfo", JSON.stringify({ ...base, features: Array.from({ length: 64 }, (_, i) => feature(i)) }), "response").features.length, 64);
  assert.throws(() => decodeManagement("ServiceInfo", JSON.stringify({ ...base, features: Array.from({ length: 65 }, (_, i) => feature(i)) }), "response"), ManagementWireError);
});

test("public discovery codecs require the version-one profile and preserve lexical integer advertisement boundaries", () => {
  for (const profile of ["legacy-v1", "complete-v2"]) {
    assert.throws(() => decodeManagement("ServiceFeaturePage", JSON.stringify({ profile, items: [] }), "response"), ManagementWireError);
    assert.throws(() => decodeManagement("ApplicationApiCapabilityPage", JSON.stringify({ applicationId: app, profile, items: [] }), "response"), ManagementWireError);
  }
  for (const token of ["1.0", "1e0"]) {
    assert.throws(() => decodeManagement("ServiceInfo", `{"serviceVersion":"1","protocolMajors":[1],"features":[],"apiDiscoveryVersion":${token}}`, "response"), ManagementWireError);
    assert.throws(() => decodeManagement("Capabilities", `{"applicationId":"${app}","implementedApis":[],"callerHints":[],"apiDiscoveryVersion":${token}}`, "response"), ManagementWireError);
  }
});

test("pre-cancelled complete discovery performs no transport or credential work", async () => {
  let touched = 0; const controller = new AbortController(); controller.abort();
  const client = new ManagementClient({ baseUrl: "https://example.test/", credentials: async () => { touched++; return { kind: "bearer", token: "unused-test-token" }; },
    transport: { async send() { touched++; throw new Error("not reached"); } } });
  assert.equal((await client.listServiceFeatures(undefined, { signal: controller.signal })).kind, "canceled");
  assert.equal(touched, 0);
});

function response(request, body, dispose = async () => {}) {
  return { status: 200, url: request.url, redirected: false, headers: { "content-type": "application/json" },
    body: new ReadableStream({ start(controller) { controller.enqueue(new TextEncoder().encode(JSON.stringify(body))); controller.close(); } }), dispose };
}
