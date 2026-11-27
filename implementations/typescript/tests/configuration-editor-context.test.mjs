import test from "node:test";
import assert from "node:assert/strict";
import { ConfigurationWireError, ManagementConfigurationAdministrationClient, decodeConfiguration, encodeConfiguration } from "@cislib/management-client";

const app = "11111111-1111-4111-8111-111111111111";
const binding = "22222222-2222-4222-8222-222222222222";
const common = "33333333-3333-4333-8333-333333333333";
const application = "44444444-4444-4444-8444-444444444444";
const defaultsJson = '{ "Count":9007199254740993,"Greeting":"Café 日本語","Enabled":false,"Zero":0,"Optional":null }';
function fixture() {
  return { bindingId: binding, applicationId: app, application: "CountryData", environment: "staging", configurationId: "settings",
    schemaVersion: "9007199254740993", hostApplicationName: "Demo.CountryData", shape: { kind: "object", nullable: false, members: [] },
    selectedSource: "application", commonSource: { state: "available", sourceId: common },
    applicationSource: { state: "available", sourceId: application }, defaults: { state: "available", defaultsJson } };
}
function response(request, value, dispose = async () => {}, status = 200) {
  return { status, headers: { "content-type": status === 200 ? "application/json" : "application/problem+json" }, url: request.url, redirected: false,
    body: new ReadableStream({ start(controller) { controller.enqueue(new TextEncoder().encode(JSON.stringify(value))); controller.close(); } }), dispose };
}

test("editor context retains exact root defaults and schema identity without number or Unicode round trips", async () => {
  const value = fixture();
  value.futureMetadata = { compatible: true };
  const decoded = await decodeConfiguration("ConfigurationEditorContext", JSON.stringify(value), "response");
  assert.equal(decoded.defaults.defaultsJson, defaultsJson);
  assert.equal(decoded.schemaVersion, "9007199254740993");
  assert.ok(Object.isFrozen(decoded.shape.members));
  assert.ok(Object.isFrozen(decoded.defaults));
  assert.deepEqual(await decodeConfiguration("ConfigurationEditorContext",
    await encodeConfiguration("ConfigurationEditorContext", decoded, "response"), "response"), decoded);
});

test("editor source absence redaction and authorized disabled identity stay separate from publication absence", async () => {
  for (const [slot, defaults] of [
    [{ state: "absent" }, { state: "unavailable" }],
    [{ state: "redacted" }, { state: "redacted" }],
    [{ state: "unavailable", sourceId: application }, { state: "unavailable" }],
    [{ state: "unavailable" }, { state: "unavailable" }],
    [{ state: "available", sourceId: application }, { state: "redacted" }]
  ]) {
    const value = fixture(); value.applicationSource = slot; value.defaults = defaults;
    const decoded = await decodeConfiguration("ConfigurationEditorContext", JSON.stringify(value), "response");
    assert.deepEqual(decoded.applicationSource, slot); assert.deepEqual(decoded.defaults, defaults);
    assert.equal("publications" in decoded, false);
  }
});

test("malformed source/default correlations cannot become authorized empty or publishable content", async () => {
  const changes = [
    value => { value.applicationSource = { state: "redacted", sourceId: application }; },
    value => { value.applicationSource = { state: "absent" }; },
    value => { value.applicationSource = { state: "unavailable", sourceId: application }; },
    value => { value.defaults = { state: "redacted", defaultsJson: "{}" }; },
    value => { value.applicationSource = { state: "future" }; },
    value => { value.commonSource.sourceId = application; },
    value => { value.hostApplicationName = "CommonConfiguration"; },
    value => { value.selectedSource = "Common"; },
    value => { value.defaults.defaultsJson = "[]"; },
    value => { value.defaults.defaultsJson = '{"duplicate":1,"duplicate":2}'; },
    value => { value.defaults.defaultsJson = '{"text":"' + "x".repeat(262144) + '"}'; }
  ];
  for (const change of changes) { const value = fixture(); change(value);
    await assert.rejects(decodeConfiguration("ConfigurationEditorContext", JSON.stringify(value), "response"), ConfigurationWireError); }
});

test("exact optional editor read uses current credentials prefix target source and joins its response", async () => {
  let credentials = 0; let disposals = 0; const requests = [];
  const client = new ManagementConfigurationAdministrationClient({ baseUrl: "https://example.test/prefix", credentials: async () => {
    credentials++; return { kind: "bearer", token: "controlled" };
  }, transport: { async send(request) { requests.push(request); return response(request, fixture(), async () => { disposals++; }); } } });
  const result = await client.getBindingEditorContext(app, "settings", "9007199254740993", "application");
  assert.equal(result.kind, "http"); assert.equal(result.cleanupSucceeded, true); assert.equal(result.value.defaults.defaultsJson, defaultsJson);
  assert.equal(credentials, 1); assert.equal(disposals, 1); assert.equal(requests.length, 1);
  assert.equal(requests[0].url, `https://example.test/prefix/management/v1/applications/${app}/configuration/settings/editor-context?schemaVersion=9007199254740993&source=application`);
  assert.equal(requests[0].method, "GET"); assert.equal(requests[0].maximumResponseBytes, 1048576);
});

test("editor read rejects mismatched selected target before returning protected content", async () => {
  for (const change of [value => { value.applicationId = binding; }, value => { value.configurationId = "other"; },
    value => { value.schemaVersion = "1"; }, value => { value.selectedSource = "common"; }]) {
    let disposed = 0; const value = fixture(); change(value);
    const client = new ManagementConfigurationAdministrationClient({ baseUrl: "https://example.test/", transport: { async send(request) {
      return response(request, value, async () => { disposed++; });
    } } });
    const result = await client.getBindingEditorContext(app, "settings", "9007199254740993", "application");
    assert.equal(result.kind, "protocolFailure"); assert.equal(result.value, undefined); assert.equal(disposed, 1);
  }
});

test("invalid editor selectors never invoke transport or credentials", async () => {
  let calls = 0;
  const client = new ManagementConfigurationAdministrationClient({ baseUrl: "https://example.test/", credentials: async () => { calls++; throw Error("unexpected credentials"); },
    transport: { async send() { calls++; throw Error("unexpected transport"); } } });
  for (const [target, config, schema, source] of [[app, "settings", "1", "future"], [app, "../other", "1", "common"],
    [app, "settings", "01", "application"], ["not-a-uuid", "settings", "1", "application"]]) {
    await assert.rejects(client.getBindingEditorContext(target, config, schema, source), TypeError);
  }
  assert.equal(calls, 0);
});

test("a selected tighter depth also bounds the optional editor response", async () => {
  const value = fixture();
  value.shape.members.push({ name: "Greeting", required: false, shape: { kind: "string", nullable: false } });
  let disposed = 0;
  const client = new ManagementConfigurationAdministrationClient({ baseUrl: "https://example.test/", wireLimits: { maximumJsonDepth: 3 },
    transport: { async send(request) { return response(request, value, async () => { disposed++; }); } } });
  const result = await client.getBindingEditorContext(app, "settings", "9007199254740993", "application");
  assert.equal(result.kind, "protocolFailure"); assert.equal(result.value, undefined); assert.equal(disposed, 1);
});

test("optional unsupported editor context stays explicit without source or defaults fallback", async () => {
  const problem = { type: "https://cislib.dev/problems/unsupported", title: "Unsupported", status: 501, code: "management.configuration.editor.unsupported" };
  let calls = 0;
  const client = new ManagementConfigurationAdministrationClient({ baseUrl: "https://example.test/", transport: { async send(request) {
    calls++; return response(request, problem, undefined, 501);
  } } });
  const result = await client.getBindingEditorContext(app, "settings", "1", "common");
  assert.equal(result.kind, "http"); assert.equal(result.status, 501); assert.deepEqual(result.problem, problem);
  assert.equal(result.value, undefined); assert.equal(calls, 1);
});

test("canceling during cleanup joins it and preserves the already decoded HTTP context", async () => {
  let joinCleanup; let cleanupEntered;
  const entered = new Promise(resolve => { cleanupEntered = resolve; });
  const joined = new Promise(resolve => { joinCleanup = resolve; });
  const controller = new AbortController();
  const client = new ManagementConfigurationAdministrationClient({ baseUrl: "https://example.test/", transport: { async send(request) {
    return response(request, fixture(), async () => { cleanupEntered(); await joined; });
  } } });
  let settled = false;
  const pending = client.getBindingEditorContext(app, "settings", "9007199254740993", "application", { signal: controller.signal });
  void pending.then(() => { settled = true; });
  await entered; controller.abort(); await new Promise(resolve => setImmediate(resolve));
  assert.equal(settled, false);
  joinCleanup(); const result = await pending;
  assert.equal(result.kind, "http"); assert.equal(result.value.defaults.defaultsJson, defaultsJson); assert.equal(result.cleanupSucceeded, true);
});
