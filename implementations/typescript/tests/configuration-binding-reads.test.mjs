import test from "node:test";
import assert from "node:assert/strict";
import { ConfigurationWireError, ManagementConfigurationAdministrationClient, decodeConfiguration } from "@cislib/management-client";

const app = "11111111-1111-4111-8111-111111111111";
const other = "22222222-2222-4222-8222-222222222222";
const publication = "33333333-3333-4333-8333-333333333333";
const schema = "9007199254740993";
function release() {
  return { releaseId: other, applicationId: app, configurationId: "settings", schemaVersion: schema, environment: "staging",
    commonSource: { state: "present", publicationId: publication }, applicationSource: { state: "absent" },
    actor: "operator", createdAtUtc: "2026-11-26T11:00:00.1234567Z", reason: "Retained reviewed content", eTag: '"release-etag"' };
}
function adoption() {
  return { applicationId: app, environment: "staging", configurationId: "settings", schemaVersion: schema,
    registrationFreshness: "noInstances", adoptionFreshness: "unknown", convergence: "noInstances" };
}
function response(request, value, dispose = async () => {}, status = 200) {
  return { status, url: request.url, redirected: false,
    headers: { "content-type": status === 200 ? "application/json" : "application/problem+json" },
    body: new ReadableStream({ start(controller) { controller.enqueue(new TextEncoder().encode(JSON.stringify(value))); controller.close(); } }), dispose };
}

test("binding release and adoption reads use exact scoped bounded GETs and current per-call credentials", async () => {
  const sent = []; let credentials = 0; let disposed = 0;
  const client = new ManagementConfigurationAdministrationClient({ baseUrl: "https://example.test/prefix", credentials: async () => {
    credentials++; return { kind: "bearer", token: "current" };
  }, transport: { async send(request) {
    sent.push(request); return response(request, { items: [request.url.includes("/releases?") ? release() : adoption()], nextCursor: "opaque-next" }, async () => { disposed++; });
  } } });
  const releases = await client.getReleases(app, "settings", schema, { pageSize: 1, cursor: "opaque-before" });
  const status = await client.getBindingStatus(app, "settings", schema, { pageSize: 1 });
  assert.equal(releases.kind, "http"); assert.equal(releases.value.items[0].schemaVersion, schema);
  assert.equal(releases.value.items[0].createdAtUtc, release().createdAtUtc);
  assert.equal(status.value.items[0].convergence, "noInstances"); assert.equal(status.value.items[0].registrationId, undefined);
  assert.ok(Object.isFrozen(releases.value.items[0].commonSource));
  assert.equal(credentials, 2); assert.equal(disposed, 2);
  assert.equal(sent[0].url, `https://example.test/prefix/management/v1/applications/${app}/configuration/settings/releases?schemaVersion=${schema}&pageSize=1&cursor=opaque-before`);
  assert.equal(sent[1].url, `https://example.test/prefix/management/v1/applications/${app}/configuration/settings/status?schemaVersion=${schema}&pageSize=1`);
  for (const request of sent) { assert.equal(request.method, "GET"); assert.equal(request.body, undefined); assert.equal(request.headers["Idempotency-Key"], undefined); }
  assert.equal(releases.mutationUncertain, false); assert.equal(status.mutationUncertain, false);
});

test("wire-valid foreign application definition or schema rows never cross either exact read", async () => {
  for (const method of ["getReleases", "getBindingStatus"]) {
    for (const [field, value] of [["applicationId", other], ["configurationId", "other"], ["schemaVersion", "1"]]) {
      const row = method === "getReleases" ? release() : adoption(); row[field] = value;
      await decodeConfiguration(method === "getReleases" ? "ConfigurationReleasePage" : "ConfigurationAdoptionPage", JSON.stringify({ items: [row] }), "response");
      let disposed = 0;
      const client = new ManagementConfigurationAdministrationClient({ baseUrl: "https://example.test", transport: { async send(request) {
        return response(request, { items: [row] }, async () => { disposed++; });
      } } });
      const result = await client[method](app, "settings", schema);
      assert.equal(result.kind, "protocolFailure"); assert.equal(result.code, "response.configuration_echo_mismatch");
      assert.equal(result.value, undefined); assert.equal(disposed, 1);
    }
  }
});

test("release pages retain exact facts while malformed references and oversized pages fail", async () => {
  const page = await decodeConfiguration("ConfigurationReleasePage", JSON.stringify({ items: [release()] }), "response");
  assert.equal(page.items[0].schemaVersion, schema); assert.equal("version" in page.items[0], false);
  for (const value of [{ items: Array.from({ length: 501 }, release) }, { items: [release(), release()] },
    { items: [{ ...release(), commonSource: { state: "absent", publicationId: publication } }] },
    { items: [{ ...release(), schemaVersion: 9007199254740993 }] }, { items: [release()], nextCursor: "contains space" }]) {
    await assert.rejects(decodeConfiguration("ConfigurationReleasePage", JSON.stringify(value), "response"), ConfigurationWireError);
  }
});

test("each scoped read honors lower response body and depth limits with joined disposal", async () => {
  for (const method of ["getReleases", "getBindingStatus"]) {
    for (const wireLimits of [{ maximumBodyBytes: 128 }, { maximumJsonDepth: 1 }]) {
      let disposed = 0;
      const client = new ManagementConfigurationAdministrationClient({ baseUrl: "https://example.test", wireLimits, transport: { async send(request) {
        return response(request, { items: [method === "getReleases" ? release() : adoption()] }, async () => { disposed++; });
      } } });
      const result = await client[method](app, "settings", schema);
      assert.equal(result.kind, "protocolFailure"); assert.equal(result.value, undefined); assert.equal(disposed, 1);
    }
  }
  for (const wireLimits of [{ maximumBodyBytes: 128 }, { maximumJsonDepth: 1 }]) {
    let disposed = 0;
    const client = new ManagementConfigurationAdministrationClient({ baseUrl: "https://example.test", wireLimits,
      transport: { async send(request) { return response(request, { type: "about:blank", title: "Unavailable", status: 501,
        code: "management.configuration.binding_read.unsupported", detail: "x".repeat(200), future: { nested: true } }, async () => { disposed++; }, 501); } } });
    const result = await client.getReleases(app, "settings", schema);
    assert.equal(result.kind, "protocolFailure"); assert.equal(result.problem, undefined); assert.equal(disposed, 1);
  }
});

test("deadline signals but cannot settle a scoped read before its actual response cleanup", async () => {
  let enter; let releaseCleanup; let settled = false; let signal;
  const entered = new Promise(resolve => { enter = resolve; });
  const held = new Promise(resolve => { releaseCleanup = resolve; });
  const client = new ManagementConfigurationAdministrationClient({ baseUrl: "https://example.test", timeoutMs: 25,
    transport: { async send(request) { signal = request.signal; return response(request, { items: [release()] }, async () => { enter(); await held; }); } } });
  const call = client.getReleases(app, "settings", schema); void call.then(() => { settled = true; }, () => { settled = true; });
  try {
    await entered; await new Promise(resolve => setTimeout(resolve, 60));
    assert.equal(signal.aborted, true); assert.equal(settled, false);
  } finally { releaseCleanup(); }
  const result = await call;
  // The response was decoded before cancellation. As with the existing candidate read,
  // later cancellation cannot erase that known observation; cleanup still had to join.
  assert.equal(result.kind, "http"); assert.equal(result.value.items[0].releaseId, other);
  assert.equal(result.cleanupSucceeded, true); assert.equal(result.mutationUncertain, false);
});

test("unsupported optional backend stays a typed problem rather than an empty successful page", async () => {
  let sends = 0;
  const client = new ManagementConfigurationAdministrationClient({ baseUrl: "https://example.test", transport: { async send(request) {
    sends++; return response(request, { type: "about:blank", title: "Unavailable", status: 501,
      code: "management.configuration.binding_read.unsupported" }, async () => {}, 501);
  } } });
  for (const method of ["getReleases", "getBindingStatus"]) {
    const result = await client[method](app, "settings", schema);
    assert.equal(result.status, 501); assert.equal(result.value, undefined); assert.equal(result.cleanupSucceeded, true);
    assert.equal(result.problem.code, "management.configuration.binding_read.unsupported");
  }
  assert.equal(sends, 2);
});

test("caller page caps bound default admission and returned rows on both reads", async () => {
  for (const method of ["getReleases", "getBindingStatus"]) {
    let credentials = 0, sends = 0, disposed = 0;
    const client = new ManagementConfigurationAdministrationClient({ baseUrl: "https://example.test", wireLimits: { maximumPageSize: 1 },
      credentials: async () => { credentials++; return { kind: "none" }; }, transport: { async send(request) {
        sends++; assert.equal(new URL(request.url).searchParams.get("pageSize"), "1");
        const rows = method === "getReleases" ? [release(), { ...release(), releaseId: "44444444-4444-4444-8444-444444444444" }]
          : [adoption(), { ...adoption(), cohortId: "canary" }];
        return response(request, { items: rows }, async () => { disposed++; });
      } } });
    await assert.rejects(client[method](app, "settings", schema, { pageSize: 2 }), TypeError);
    assert.equal(credentials, 0); assert.equal(sends, 0);
    const result = await client[method](app, "settings", schema);
    assert.equal(result.kind, "protocolFailure"); assert.equal(result.code, "response.configuration_page_limit");
    assert.equal(result.value, undefined); assert.equal(credentials, 1); assert.equal(sends, 1); assert.equal(disposed, 1);
  }
});
