import assert from "node:assert/strict";
import test from "node:test";
import { ClientTransportFailure } from "@cislib/client-core";
import { ConfigurationWireError, ManagementConfigurationAdministrationClient, decodeConfiguration, knownConfigurationOrigin } from "@cislib/management-client";

const app = "11111111-1111-4111-8111-111111111111", other = "22222222-2222-4222-8222-222222222222";
const source = "33333333-3333-4333-8333-333333333333", binding = "44444444-4444-4444-8444-444444444444", key = "original-create-key";
const command = { commandVersion: 1, configurationId: "custom.settings", intent: "createGeneric", reason: "Create application settings" };
const context = { applicationId: app, application: "country-data", environment: "Development", eTag: '"creation-context"', schemaVersion: "1", hostApplicationName: "CountryData" };
const summary = { applicationId: app, environment: "Development", configurationId: "custom.settings", schemaVersion: "1", sourceId: source,
  bindingId: binding, hostApplicationName: "CountryData", origin: "unassigned", hasCohortOverrides: false };
function original() {
  return { operation: { name: "management.configuration.create", scope: `management.configuration.v1/management.configuration.create/${app}/management.actor.v1.${"a".repeat(64)}`, key },
    resultSource: "prepared", originalCompletion: "committed", result: { commandVersion: 1, kind: "configurationCreated", decision: "applied", code: "created",
      decidedAtUtc: "2026-11-26T12:00:00.0000000Z", applicationId: app, configurationId: "custom.settings", schemaVersion: "1", sourceId: source,
      bindingId: binding, hostApplicationName: "CountryData", sourceETag: '"configuration.source.1"', eTag: '"creation-context-next"' } };
}
function response(request, value, status = 200, dispose = async () => {}) {
  return { status, url: request.url, redirected: false, headers: { "content-type": status < 300 ? "application/json" : "application/problem+json" },
    body: new ReadableStream({ start(controller) { controller.enqueue(new TextEncoder().encode(JSON.stringify(value))); controller.close(); } }), dispose };
}

test("summary publication dates survive the actual typed client without timezone or fractional precision conversion", async () => {
  const modifiedAtUtc = "2026-11-26T12:00:00.1234567Z";
  const client = new ManagementConfigurationAdministrationClient({ baseUrl: "https://management.example.test", credentials: async () => ({ kind: "none" }),
    transport: { async send(request) { return response(request, { items: [{ ...summary, origin: "shared", modifiedAtUtc }] }); } } });
  const result = await client.getConfigurationSummaries(app, { pageSize: 1 });
  assert.equal(result.kind, "http");
  assert.equal(result.status, 200);
  assert.equal(result.value.items[0].modifiedAtUtc, modifiedAtUtc);
  assert.ok(Object.isFrozen(result.value.items[0]));
  await assert.rejects(decodeConfiguration("ConfigurationSummary", JSON.stringify({ ...summary, modifiedAtUtc: "2026-11-26T12:00:00+00:00" }), "response"), ConfigurationWireError);
});

test("creation context write recovery and summaries are exact finite calls with fresh credentials and joined disposal", async () => {
  const sent = []; let credentials = 0, disposed = 0;
  const client = new ManagementConfigurationAdministrationClient({ baseUrl: "https://management.example.test/prefix", credentials: async () => { credentials++; return { kind: "none" }; },
    transport: { async send(request) { sent.push(request); return response(request, request.url.endsWith("/context") ? context
      : request.url.includes("/configuration-summaries?") ? { items: [summary], nextCursor: "opaque-next" } : original(), 200, async () => { disposed++; }); } } });
  assert.equal((await client.getCreationContext(app)).value.hostApplicationName, "CountryData");
  assert.equal((await client.createConfiguration(app, key, context.eTag, command)).value.result.sourceId, source);
  assert.equal((await client.getCreationOperation(app, key)).value.originalCompletion, "committed");
  assert.equal((await client.getConfigurationSummaries(app, { pageSize: 1, cursor: "opaque-before" })).value.items[0].origin, "unassigned");
  assert.deepEqual(sent.map(request => request.method), ["GET", "POST", "GET", "GET"]);
  assert.equal(sent[0].url, `https://management.example.test/prefix/management/v1/applications/${app}/configuration-creations/context`);
  assert.equal(sent[2].url, `https://management.example.test/prefix/management/v1/applications/${app}/configuration-creation-operations/${key}`);
  assert.equal(sent[3].url, `https://management.example.test/prefix/management/v1/applications/${app}/configuration-summaries?pageSize=1&cursor=opaque-before`);
  assert.equal(sent[1].headers["If-Match"], context.eTag); assert.equal(sent[1].headers["Idempotency-Key"], key);
  assert.deepEqual(JSON.parse(new TextDecoder().decode(sent[1].body)), command);
  assert.equal(sent[2].body, undefined); assert.equal(sent[2].headers["If-Match"], undefined); assert.equal(credentials, 4); assert.equal(disposed, 4);
});

test("closed creation commands and exact preconditions fail before credentials or transport; writes never retry", async () => {
  let credentials = 0, sends = 0;
  const client = new ManagementConfigurationAdministrationClient({ baseUrl: "https://management.example.test", readRetries: 2,
    credentials: async () => { credentials++; return { kind: "none" }; }, transport: { async send() { sends++; throw new ClientTransportFailure("network.lost"); } } });
  for (const invalid of [undefined, { ...command, payloadJson: "{}" }, { ...command, intent: "deriveSchema" }, { ...command, configurationId: ".." }]) {
    await assert.rejects(client.createConfiguration(app, key, context.eTag, invalid));
  }
  await assert.rejects(client.createConfiguration(app, key, 'W/"weak"', command), TypeError);
  assert.equal(credentials, 0); assert.equal(sends, 0);
  const result = await client.createConfiguration(app, key, context.eTag, command);
  assert.equal(result.kind, "transportFailure"); assert.equal(sends, 1); assert.equal(credentials, 1); assert.equal(result.mutationUncertain, true);
});

test("creation echoes refuse foreign app family resource key and submitted configuration even inside problems", async () => {
  for (const mutate of [value => { value.operation.scope = value.operation.scope.replace(app, other); }, value => { value.operation.key = "another-key"; },
    value => { value.operation.name = "management.configuration.source.publish"; }, value => { value.operation.scope = app; },
    value => { value.result.applicationId = other; }, value => { value.result.configurationId = "another.settings"; }]) {
    const value = original(); mutate(value);
    for (const status of [200, 503]) {
      let disposed = 0;
      const client = new ManagementConfigurationAdministrationClient({ baseUrl: "https://management.example.test", transport: { async send(request) {
        return response(request, status === 200 ? value : { type: "about:blank", title: "Cleanup failed", status, code: "cleanup.failed", operation: value.operation, result: value }, status,
          async () => { disposed++; });
      } } });
      const result = await client.createConfiguration(app, key, context.eTag, command);
      assert.equal(result.kind, "protocolFailure"); assert.equal(result.value, undefined); assert.equal(result.problem, undefined); assert.equal(disposed, 1);
    }
  }
});

test("committed creation survives current error and cleanup failure without becoming a new business failure", async () => {
  let disposed = 0;
  const value = original(); value.resultSource = "found";
  const client = new ManagementConfigurationAdministrationClient({ baseUrl: "https://management.example.test", transport: { async send(request) {
    return response(request, { type: "about:blank", title: "Read cleanup failed", status: 503, code: "cleanup.failed", operation: value.operation, result: value }, 503,
      async () => { disposed++; throw new ClientTransportFailure("cleanup.failed"); });
  } } });
  const result = await client.getCreationOperation(app, key);
  assert.equal(result.problem.result.originalCompletion, "committed"); assert.equal(result.problem.result.result.kind, "configurationCreated");
  assert.equal(result.problem.result.result.sourceId, source); assert.equal(result.cleanupSucceeded, false); assert.equal(disposed, 1);
});

test("summary unknown origins stay unknown while duplicate identities and foreign targets cannot enter the page", async () => {
  assert.equal(knownConfigurationOrigin("future-origin").isKnown, false);
  const page = await decodeConfiguration("ConfigurationSummaryPage", JSON.stringify({ items: [{ ...summary, origin: "future-origin" }] }), "response");
  assert.equal(page.items[0].origin, "future-origin"); assert.equal(page.items[0].payloadJson, undefined);
  for (const second of [{ ...summary, bindingId: other, configurationId: "another" }, { ...summary, sourceId: other, configurationId: "another" },
    { ...summary, bindingId: other, sourceId: other }]) {
    await assert.rejects(decodeConfiguration("ConfigurationSummaryPage", JSON.stringify({ items: [summary, second] }), "response"), ConfigurationWireError);
  }
  const client = new ManagementConfigurationAdministrationClient({ baseUrl: "https://management.example.test", transport: { async send(request) {
    return response(request, { items: [{ ...summary, applicationId: other }] });
  } } });
  const result = await client.getConfigurationSummaries(app);
  assert.equal(result.kind, "protocolFailure"); assert.equal(result.code, "response.configuration_echo_mismatch");
});

test("summary caller page bounds apply before credentials and to returned rows; optional unsupported stays explicit", async () => {
  let credentials = 0, sends = 0;
  const client = new ManagementConfigurationAdministrationClient({ baseUrl: "https://management.example.test", wireLimits: { maximumPageSize: 1 },
    credentials: async () => { credentials++; return { kind: "none" }; }, transport: { async send(request) { sends++;
      assert.equal(new URL(request.url).searchParams.get("pageSize"), "1");
      return response(request, { items: [summary, { ...summary, configurationId: "other", sourceId: other, bindingId: app }] });
    } } });
  await assert.rejects(client.getConfigurationSummaries(app, { pageSize: 2 }), TypeError);
  assert.equal(credentials, 0); assert.equal(sends, 0);
  assert.equal((await client.getConfigurationSummaries(app)).code, "response.configuration_page_limit");
  const unsupported = new ManagementConfigurationAdministrationClient({ baseUrl: "https://management.example.test", transport: { async send(request) {
    return response(request, { type: "about:blank", title: "Unsupported", status: 501, code: "configuration.creation.unsupported" }, 501);
  } } });
  const refused = await unsupported.getCreationContext(app); assert.equal(refused.status, 501); assert.equal(refused.value, undefined);
});

test("creation cleanup retains original deadline custody until the actual response owner settles", async () => {
  let enter, failEnter, release, signal, settled = false, cleanupEntered = false;
  const entered = new Promise((resolve, reject) => { enter = resolve; failEnter = reject; }), held = new Promise(resolve => { release = resolve; });
  const client = new ManagementConfigurationAdministrationClient({ baseUrl: "https://management.example.test", timeoutMs: 25, transport: { async send(request) {
    signal = request.signal; return response(request, original(), 200, async () => { cleanupEntered = true; enter(); await held; });
  } } });
  const pending = client.createConfiguration(app, key, context.eTag, command);
  void pending.then(() => { settled = true; if (!cleanupEntered) { failEnter(new Error("The owned response cleanup was never entered.")); } }, error => { settled = true; failEnter(error); });
  try { await entered; await new Promise(resolve => setTimeout(resolve, 60)); assert.equal(signal.aborted, true); assert.equal(settled, false); }
  finally { release(); }
  const result = await pending; assert.equal(result.value.originalCompletion, "committed"); assert.equal(result.cleanupSucceeded, true);
});
