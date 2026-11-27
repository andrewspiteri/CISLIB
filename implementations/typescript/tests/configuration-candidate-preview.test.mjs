import test from "node:test";
import assert from "node:assert/strict";
import { ClientTransportFailure } from "@cislib/client-core";
import { ConfigurationWireError, ManagementConfigurationAdministrationClient, computeConfigurationPayloadSha256, decodeConfiguration } from "@cislib/management-client";

const app = "11111111-1111-4111-8111-111111111111";
const binding = "22222222-2222-4222-8222-222222222222";
const source = "33333333-3333-4333-8333-333333333333";
const foreign = "44444444-4444-4444-8444-444444444444";
const payload = '{ "Large":9007199254740993,"Enabled":false,"Zero":0,"Greeting":"Café 日本語","Optional":null }';
function command() {
  return { commandVersion: 1, applicationId: app, configurationId: "settings", schemaVersion: "9007199254740993", selectedSource: "application",
    sourceId: source, sourceETag: '"source-2"', assignmentETag: '"map-0"', commonSource: { state: "absent" }, applicationSource: { state: "absent" }, payloadJson: payload };
}
async function preview(input = command()) {
  return { bindingId: binding, applicationId: app, environment: "staging", configurationId: input.configurationId, schemaVersion: input.schemaVersion,
    hostApplicationName: "Demo.CountryData", selectedSource: input.selectedSource, sourceId: input.sourceId, sourceETag: input.sourceETag,
    assignmentETag: input.assignmentETag, commonSource: { ...input.commonSource }, applicationSource: { ...input.applicationSource },
    draftSha256: await computeConfigurationPayloadSha256(input.payloadJson), structurallyValid: true, redacted: false,
    changes: [{ path: "/Enabled", kind: "changed" }], issues: [],
    currentAssignment: { bindingId: binding, applicationId: app, configurationId: input.configurationId, schemaVersion: input.schemaVersion,
      environment: "staging", eTag: input.assignmentETag, overrides: [] }, recipients: { items: [] } };
}
function response(request, value, dispose = async () => {}, status = 200) {
  return { status, url: request.url, redirected: false, headers: { "content-type": status === 200 ? "application/json" : "application/problem+json" },
    body: new ReadableStream({ start(controller) { controller.enqueue(new TextEncoder().encode(JSON.stringify(value))); controller.close(); } }), dispose };
}

test("unpublished preview retains exact draft bytes baseline references and separate preconditions", async () => {
  const expected = await preview(); let captured;
  await decodeConfiguration("ConfigurationCandidatePreview", JSON.stringify(expected), "response");
  const client = new ManagementConfigurationAdministrationClient({ baseUrl: "https://example.test/prefix", transport: { async send(request) {
    captured = request; return response(request, expected);
  } } });
  const result = await client.previewCandidate(command(), { pageSize: 3, cursor: "opaque+/" });
  assert.equal(result.kind, "http"); assert.deepEqual(result.value, expected);
  assert.equal(captured.method, "POST"); assert.equal(captured.mutating, false);
  assert.equal(captured.url, "https://example.test/prefix/management/v1/configuration/candidate-preview?pageSize=3&cursor=opaque%2B%2F");
  const sent = JSON.parse(new TextDecoder().decode(captured.body));
  assert.equal(sent.payloadJson, payload); assert.equal(sent.schemaVersion, "9007199254740993");
  assert.equal(captured.headers["Idempotency-Key"], undefined); assert.equal(captured.headers["If-Match"], undefined);
  assert.equal("publicationId" in result.value, false);
});

test("candidate request is closed with exact integer tokens and independently bounded raw JSON", async () => {
  const raw = JSON.stringify(command());
  await assert.rejects(decodeConfiguration("ConfigurationCandidatePreviewCommand", raw.replace('"commandVersion":1', '"commandVersion":1.0'), "request"), ConfigurationWireError);
  for (const mutate of [value => { value.actor = "forged"; }, value => { value.selectedSource = "future"; }, value => { value.sourceETag = "weak"; },
    value => { value.payloadJson = "[]"; }, value => { value.payloadJson = '{"duplicate":1,"duplicate":2}'; },
    value => { value.payloadJson = '{"Text":"' + "x".repeat(262144) + '"}'; }]) {
    const value = command(); mutate(value);
    await assert.rejects(decodeConfiguration("ConfigurationCandidatePreviewCommand", JSON.stringify(value), "request"), ConfigurationWireError);
  }
});

test("Configuration byte and string envelopes both reject a BOM without silently normalizing it", async () => {
  for (const [schema, value, direction] of [["ConfigurationCandidatePreviewCommand", command(), "request"],
    ["ConfigurationCandidatePreview", await preview(), "response"]]) {
    const text = "\ufeff" + JSON.stringify(value);
    await assert.rejects(decodeConfiguration(schema, text, direction), ConfigurationWireError);
    await assert.rejects(decodeConfiguration(schema, new TextEncoder().encode(text), direction), ConfigurationWireError);
  }
});

test("candidate response identity map and digest cannot be substituted before returning its value", async () => {
  for (const mutate of [value => { value.applicationId = foreign; value.currentAssignment.applicationId = foreign; },
    value => { value.sourceId = foreign; }, value => { value.selectedSource = "common"; }, value => { value.sourceETag = '"different"'; },
    value => { value.assignmentETag = '"different"'; value.currentAssignment.eTag = '"different"'; }, value => { value.draftSha256 = "0".repeat(64); },
    value => { value.commonSource = { state: "present", publicationId: foreign }; }]) {
    const value = await preview(); mutate(value); let disposed = 0;
    await decodeConfiguration("ConfigurationCandidatePreview", JSON.stringify(value), "response");
    const client = new ManagementConfigurationAdministrationClient({ baseUrl: "https://example.test/", transport: { async send(request) {
      return response(request, value, async () => { disposed++; });
    } } });
    const result = await client.previewCandidate(command());
    assert.equal(result.kind, "protocolFailure"); assert.equal(result.value, undefined); assert.equal(disposed, 1);
  }
});

test("mutable caller objects cannot rewrite the reviewed request or its subsequent response checks", async () => {
  let release; let credentialEntered;
  const entered = new Promise(resolve => { credentialEntered = resolve; }); const held = new Promise(resolve => { release = resolve; });
  const value = command(); const expected = await preview(value); let sent;
  const client = new ManagementConfigurationAdministrationClient({ baseUrl: "https://example.test/", credentials: async () => { credentialEntered(); await held; return { kind: "none" }; },
    transport: { async send(request) { sent = JSON.parse(new TextDecoder().decode(request.body)); return response(request, expected); } } });
  const pending = client.previewCandidate(value); await entered;
  value.payloadJson = '{"mutated":true}'; value.sourceId = foreign; value.commonSource.state = "present"; value.commonSource.publicationId = foreign;
  release(); const result = await pending;
  assert.equal(result.kind, "http"); assert.equal(sent.payloadJson, payload); assert.equal(sent.sourceId, source); assert.deepEqual(sent.commonSource, { state: "absent" });
});

test("read-only candidate POST neither retries automatically nor becomes an uncertain mutation", async () => {
  let calls = 0;
  const client = new ManagementConfigurationAdministrationClient({ baseUrl: "https://example.test/", readRetries: 2, transport: { async send() {
    calls++; throw new ClientTransportFailure("controlled.unavailable");
  } } });
  const result = await client.previewCandidate(command());
  assert.equal(calls, 1); assert.equal(result.kind, "transportFailure"); assert.equal(result.mutationUncertain, false); assert.equal(result.operationKey, undefined);
});

test("canceling after candidate decode waits for response cleanup and preserves its known read", async () => {
  const value = await preview(); let entered; let release; let settled = false;
  const disposing = new Promise(resolve => { entered = resolve; }); const held = new Promise(resolve => { release = resolve; });
  const controller = new AbortController();
  const client = new ManagementConfigurationAdministrationClient({ baseUrl: "https://example.test/", transport: { async send(request) {
    return response(request, value, async () => { entered(); await held; });
  } } });
  const pending = client.previewCandidate(command(), undefined, { signal: controller.signal }); void pending.then(() => { settled = true; });
  await disposing; controller.abort(); await new Promise(resolve => setImmediate(resolve)); assert.equal(settled, false);
  release(); const result = await pending; assert.equal(result.kind, "http"); assert.equal(result.value.draftSha256, value.draftSha256);
});

test("unsupported candidate preview remains an explicit problem with no publication or defaults fallback", async () => {
  const problem = { type: "about:blank", title: "Unsupported", status: 501, code: "management.configuration.candidate.unsupported" };
  const client = new ManagementConfigurationAdministrationClient({ baseUrl: "https://example.test/", transport: { async send(request) { return response(request, problem, undefined, 501); } } });
  const result = await client.previewCandidate(command()); assert.equal(result.status, 501); assert.deepEqual(result.problem, problem); assert.equal(result.value, undefined);
});
