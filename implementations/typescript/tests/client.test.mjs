import test from "node:test";
import assert from "node:assert/strict";
import { ManagementClient, captureBaseUrl } from "@cislib/management-client";
import { createFetchTransport, ClientProtocolFailure, ClientTransportFailure } from "@cislib/client-core";

const id = "11111111-1111-1111-1111-111111111111";
const registrationId = "22222222-2222-2222-2222-222222222222";
const timestamp = "2026-11-23T12:34:56.1234567Z";
const application = { applicationId: id, application: "worker", environment: "test", metadata: { displayName: "Café" }, metadataRevision: "9007199254740993", metadataETag: '"metadata-9007199254740993"' };
const operation = { name: "management.metadata.replace", scope: "opaque-scope", key: "original-key" };
const metadataResult = { operation, resultSource: "prepared", originalCompletion: "committed", result: { commandVersion: 1, applicationId: id, decision: "applied", code: "metadata.applied", metadata: application.metadata, metadataRevision: application.metadataRevision, metadataETag: application.metadataETag, decidedAtUtc: timestamp } };
const info = { serviceVersion: "1.0", protocolMajors: [1], features: [] };
const inventory = { sequence: "1", observedAtUtc: timestamp, modules: [], categories: [], jobs: [] };
const observation = { sequence: "1", observedAtUtc: timestamp, lifecycle: "ready", health: "healthy" };

test("all twelve typed methods preserve HTTPS prefix and explicit command headers", async () => {
  const cases = [
    [c => c.getServiceInfo(), "GET", "service-info", info],
    [c => c.register(registrationId, { protocolMajor: 1, reportedInstanceId: "replica", processStartedAtUtc: timestamp, applicationVersion: "1" }), "PUT", `registrations/${registrationId}`, { registrationId, applicationId: id, registeredAtUtc: timestamp }],
    [c => c.reportObservation(registrationId, observation), "PUT", `registrations/${registrationId}/observation`, { registrationId, stream: "observation", submittedSequence: "1", acceptedSequence: "1", disposition: "accepted", receivedAtUtc: timestamp }],
    [c => c.reportInventory(registrationId, inventory), "PUT", `registrations/${registrationId}/inventory`, { registrationId, stream: "inventory", submittedSequence: "1", acceptedSequence: "1", disposition: "accepted", receivedAtUtc: timestamp }],
    [c => c.closeRegistration(registrationId), "DELETE", `registrations/${registrationId}`, { registrationId, closedAtUtc: timestamp }],
    [c => c.listApplications({ pageSize: 2, cursor: "opaque+/?" }), "GET", "applications?pageSize=2&cursor=opaque%2B%2F%3F", { items: [application], nextCursor: "opaque" }],
    [c => c.getApplication(id), "GET", `applications/${id}`, application],
    [c => c.listInstances(id), "GET", `applications/${id}/instances?pageSize=100`, { items: [] }],
    [c => c.listInventory(id), "GET", `applications/${id}/inventory?pageSize=100`, { items: [] }],
    [c => c.getCapabilities(id), "GET", `applications/${id}/capabilities`, { applicationId: id, implementedApis: [], callerHints: [] }],
    [c => c.replaceMetadata(id, operation.key, application.metadataETag, { commandVersion: 1, displayName: "Café" }), "PUT", `applications/${id}/metadata`, metadataResult],
    [c => c.getMetadataOperation(id, operation.key), "GET", `applications/${id}/metadata-operations/${operation.key}`, { ...metadataResult, resultSource: "found" }]
  ];
  for (const [invoke, method, path, body] of cases) {
    const records = [];
    const client = new ManagementClient({ baseUrl: "https://example.test/cis/prefix", transport: { async send(request) { records.push(request); return response(request, 200, body); } } });
    const result = await invoke(client);
    assert.equal(result.kind, "http"); assert.equal(result.status, 200); assert.deepEqual(result.value, body);
    assert.equal(records.length, 1); assert.equal(records[0].method, method);
    assert.equal(records[0].url, "https://example.test/cis/prefix/management/v1/" + path);
    if (method === "PUT" && path.endsWith("/metadata")) {
      assert.equal(records[0].headers["Idempotency-Key"], operation.key);
      assert.equal(records[0].headers["If-Match"], application.metadataETag);
    }
  }
});

test("base endpoint rejects ambiguous canonicalization, userinfo and non-HTTPS", () => {
  for (const value of ["http://example.test/", "https://u:p@example.test/", "https://example.test/?a=1", "https://example.test/#x", "https://example.test/a/../b", "https://example.test/a/%2e%2e/b", "https://example.test/a/%2f/b", "https://example.test/a/%5c/b", "https://example.test/\nb", "https://example.test/a/%00/b", "https://example.test/a\\b", " https://example.test/", "https://example.test/%zz"]) {
    assert.throws(() => captureBaseUrl(value), TypeError, value);
  }
  assert.equal(captureBaseUrl("https://example.test/cis/prefix"), "https://example.test/cis/prefix/");
});

test("credentials are requested per call and remain isolated across concurrent users", async () => {
  const seen = [];
  const client = new ManagementClient({ baseUrl: "https://example.test/", transport: { async send(request) { seen.push(request.authentication); await new Promise(resolve => setTimeout(resolve, 1)); return response(request, 200, info); } } });
  const forUser = token => ({ credentials: async ({ signal }) => { assert.equal(signal.aborted, false); return { kind: "bearer", token }; } });
  await Promise.all([client.getServiceInfo(forUser("alice")), client.getServiceInfo(forUser("bob"))]);
  assert.deepEqual(seen.map(value => value.token).sort(), ["alice", "bob"]);
  const anonymous = await client.getServiceInfo(); assert.equal(anonymous.status, 200); assert.deepEqual(seen[2], { kind: "none" });
});

test("read retry is explicit, finite and never retries authorization or writes", async () => {
  for (const [status, retries, method, expected] of [[503, 0, "read", 1], [503, 2, "read", 3], [401, 2, "read", 1], [403, 2, "read", 1], [500, 2, "read", 1], [503, 2, "write", 1]]) {
    let count = 0;
    const client = new ManagementClient({ baseUrl: "https://example.test/", readRetries: retries, transport: { async send(request) { count++; return response(request, status, problem(status), { "retry-after": "0" }); } } });
    const result = method === "write" ? await client.replaceMetadata(id, "original-key", '"metadata-1"', { commandVersion: 1, displayName: "Name" }) : await client.getServiceInfo();
    assert.equal(count, expected); assert.equal(result.attempts, expected); assert.equal(result.status, status);
    if (method === "write") { assert.equal(result.operationKey, "original-key"); assert.equal(result.mutationUncertain, true); }
  }
});

test("transport failure and malformed body are never read-retried", async () => {
  for (const scenario of ["network", "decode"]) {
    let count = 0;
    const client = new ManagementClient({ baseUrl: "https://example.test/", readRetries: 2, transport: { async send(request) {
      count++; if (scenario === "network") { throw new ClientTransportFailure("transport.unavailable"); }
      return response(request, 200, { incomplete: true });
    } } });
    const result = await client.getServiceInfo(); assert.equal(count, 1); assert.equal(result.kind, scenario === "network" ? "transportFailure" : "protocolFailure");
  }
});

test("unknown commit and recovery preserve original key and separate persistence from exchange", async () => {
  const unknown = { ...metadataResult, originalCompletion: "unknown" };
  const client = new ManagementClient({ baseUrl: "https://example.test/", transport: { async send(request) { return response(request, 503, { ...problem(503), operation, result: unknown }); } } });
  const result = await client.replaceMetadata(id, "original-key", '"metadata-1"', { commandVersion: 1, displayName: "Name" });
  assert.equal(result.kind, "http"); assert.equal(result.problem.result.originalCompletion, "unknown"); assert.equal(result.mutationUncertain, true); assert.equal(result.operationKey, "original-key");
});

test("redirects, foreign final URLs and oversized responses fail closed", async () => {
  for (const scenario of ["redirect", "foreign", "size"]) {
    const client = new ManagementClient({ baseUrl: "https://example.test/", wireLimits: { maximumRegularResponseBytes: 128 }, transport: { async send(request) {
      const value = response(request, scenario === "redirect" ? 302 : 200, scenario === "size" ? { ...info, extra: "x".repeat(200) } : info);
      return scenario === "foreign" ? { ...value, url: "https://other.test/management/v1/service-info" } : value;
    } } });
    const result = await client.getServiceInfo(); assert.equal(result.kind, "protocolFailure");
  }
});

test("native fetch uses no implicit browser credentials or redirect following", async () => {
  let captured;
  const transport = createFetchTransport(async (url, init) => { captured = { url, init }; return new Response(JSON.stringify(info), { status: 200, headers: { "content-type": "application/json" } }); });
  const client = new ManagementClient({ baseUrl: "https://example.test/", transport });
  assert.equal((await client.getServiceInfo({ credentials: async () => ({ kind: "bearer", token: "per-call" }) })).status, 200);
  assert.equal(captured.init.redirect, "manual"); assert.equal(captured.init.credentials, "omit"); assert.equal(captured.init.cache, "no-store"); assert.equal(captured.init.headers.get("Authorization"), "Bearer per-call");
});

test("expected cleanup failure retains established HTTP and original result", async () => {
  const client = new ManagementClient({ baseUrl: "https://example.test/", transport: { async send(request) { return { ...response(request, 200, metadataResult), async dispose() { throw new ClientTransportFailure("transport.cleanup"); } }; } } });
  const result = await client.getMetadataOperation(id, "original-key");
  assert.equal(result.kind, "http"); assert.equal(result.value.originalCompletion, "committed"); assert.equal(result.cleanupSucceeded, false);
});

function problem(status) { return { type: "urn:cis:management:error", title: "Request rejected", status, code: "request.rejected" }; }
function response(request, status, value, headers = {}) {
  return { status, url: request.url, redirected: false, headers: { "content-type": status >= 400 ? "application/problem+json" : "application/json", ...headers }, body: new ReadableStream({ start(controller) { controller.enqueue(new TextEncoder().encode(JSON.stringify(value))); controller.close(); } }), async dispose() {} };
}
