import test from "node:test";
import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import { ManagementConfigurationClient, ManagementClient, decodeConfiguration } from "@cislib/management-client";
import { ClientTransportFailure } from "@cislib/client-core";

const catalog = JSON.parse(await readFile(new URL("../../../conformance/management/v1/configuration-cases.json", import.meta.url), "utf8"));
const sample = id => JSON.parse(catalog.cases.find(row => row.id === id).json);
const snapshot = sample("configuration-snapshot-valid");
const { releaseId: _release, generation: _generation, ...target } = snapshot.desired;
const applicationId = target.applicationId;
const configurationId = target.configurationId;
const sourceId = snapshot.commonSource.sourceId;
const key = "original-configuration-key";
const ifMatch = '"current-resource"';
const families = { catalog: "management.configuration.catalog.apply", publication: "management.configuration.source.publish",
  release: "management.configuration.release.create", assignment: "management.configuration.assignment.replace" };
const sourceCommand = sample("configuration-publicationcommand-valid");
const releaseCommand = sample("configuration-releasecommand-valid");
const assignmentCommand = sample("configuration-assignmentcommand-valid");
const catalogCommand = sample("configuration-catalogcommand-valid");

test("all nineteen configuration APIs preserve configured prefix schema query pages and original command headers", async () => {
  const receipt = { registrationId: target.registrationId, stream: "configuration-status", submittedSequence: "9007199254740993",
    acceptedSequence: "9007199254740993", disposition: "accepted", receivedAtUtc: "2026-11-24T00:12:34.1234567Z" };
  const binding = `applications/${applicationId}/configuration/${configurationId}`;
  const rows = [
    [c => c.getDefinitions(), "GET", "configuration/definitions?pageSize=100", sample("configuration-definitionpage-valid")],
    [c => c.validate(sample("configuration-validationcommand-valid")), "POST", "configuration/validate", sample("configuration-validationresult-valid")],
    [c => c.preview(sample("configuration-previewcommand-valid"), { pageSize: 2, cursor: "opaque+/" }), "POST", "configuration/preview?pageSize=2&cursor=opaque%2B%2F", sample("configuration-preview-valid")],
    [c => c.getSource(sourceId), "GET", `configuration/sources/${sourceId}?pageSize=100`, sample("configuration-source-valid")],
    [c => c.publishSource(sourceId, key, ifMatch, sourceCommand), "POST", `configuration/sources/${sourceId}/publications`, original("publication"), true],
    [c => c.getPublication(snapshot.commonSource.publicationId), "GET", `configuration/publications/${snapshot.commonSource.publicationId}`, sample("configuration-publication-valid")],
    [c => c.createRelease(applicationId, configurationId, "1", key, ifMatch, releaseCommand), "POST", binding + "/releases?schemaVersion=1", original("release"), true],
    [c => c.getRelease(snapshot.desired.releaseId), "GET", `configuration/releases/${snapshot.desired.releaseId}`, sample("configuration-release-valid")],
    [c => c.getAssignments(applicationId, configurationId, "1"), "GET", binding + "/assignments?schemaVersion=1", sample("configuration-assignmentmap-valid")],
    [c => c.replaceAssignments(applicationId, configurationId, "1", key, ifMatch, assignmentCommand), "PUT", binding + "/assignments?schemaVersion=1", original("assignment"), true],
    [c => c.getRegistrationConfiguration(target), "GET", `registrations/${target.registrationId}/configuration/${configurationId}?schemaVersion=1`, snapshot],
    [c => c.reportStatus(target.registrationId, sample("configuration-statusreport-valid")), "PUT", `registrations/${target.registrationId}/configuration-status`, receipt],
    [c => c.getStatus(applicationId), "GET", `applications/${applicationId}/configuration-status?pageSize=100`, sample("configuration-adoptionpage-valid")],
    [c => c.getCatalog(), "GET", "configuration/catalog", sample("configuration-catalog-valid")],
    [c => c.applyCatalog(key, ifMatch, catalogCommand), "POST", "configuration/catalog/apply", original("catalog"), true],
    [c => c.getCatalogOperation(key), "GET", `configuration/catalog/operations/${key}`, original("catalog")],
    [c => c.getPublicationOperation(sourceId, key), "GET", `configuration/sources/${sourceId}/publication-operations/${key}`, original("publication")],
    [c => c.getReleaseOperation(applicationId, configurationId, "1", key), "GET", binding + `/release-operations/${key}?schemaVersion=1`, original("release")],
    [c => c.getAssignmentOperation(applicationId, configurationId, "1", key), "GET", binding + `/assignment-operations/${key}?schemaVersion=1`, original("assignment")]
  ];
  for (const [invoke, method, suffix, body, mutates] of rows) {
    const requests = []; let disposed = 0;
    const client = new ManagementConfigurationClient({ baseUrl: "https://example.test/prefix", transport: { async send(request) {
      requests.push(request); return { ...response(request, 200, body), async dispose() { disposed++; } };
    } } });
    const result = await invoke(client);
    assert.equal(result.kind, "http", suffix); assert.deepEqual(result.value, body); assert.equal(disposed, 1);
    assert.equal(requests.length, 1); assert.equal(requests[0].method, method);
    assert.equal(requests[0].url, "https://example.test/prefix/management/v1/" + suffix);
    if (mutates) { assert.equal(requests[0].headers["Idempotency-Key"], key); assert.equal(requests[0].headers["If-Match"], ifMatch); }
  }
});

test("read-only POST failure is non-mutating while actual POST mutation retains key and uncertainty without retries", async () => {
  for (const [invoke, mutation] of [[c => c.validate(sample("configuration-validationcommand-valid")), false],
    [c => c.preview(sample("configuration-previewcommand-valid")), false],
    [c => c.publishSource(sourceId, key, ifMatch, sourceCommand), true], [c => c.applyCatalog(key, ifMatch, catalogCommand), true]]) {
    let calls = 0;
    const client = new ManagementConfigurationClient({ baseUrl: "https://example.test/", readRetries: 2, transport: { async send() {
      calls++; throw new ClientTransportFailure("transport.unavailable");
    } } });
    const result = await invoke(client);
    assert.equal(result.kind, "transportFailure"); assert.equal(result.mutationUncertain, mutation);
    assert.equal(result.operationKey, mutation ? key : undefined); assert.equal(calls, 1);
  }
});

test("POST never retries HTTP transients and mutation HTTP uncertainty remains separate from read-only results", async () => {
  for (const [invoke, mutation] of [[c => c.validate(sample("configuration-validationcommand-valid")), false],
    [c => c.publishSource(sourceId, key, ifMatch, sourceCommand), true]]) {
    let calls = 0;
    const client = new ManagementConfigurationClient({ baseUrl: "https://example.test/", readRetries: 2, transport: { async send(request) {
      calls++; return response(request, 503, problem(503), { "content-type": "application/problem+json", "retry-after": "0" });
    } } });
    const result = await invoke(client); assert.equal(result.status, 503); assert.equal(calls, 1); assert.equal(result.mutationUncertain, mutation);
  }
});

test("identified content rejection preserves failed HTTP 200 while malformed or mismatched envelopes have none", async () => {
  for (const [id, identified] of [["configuration-payload-digest-mismatch", true], ["configuration-duplicate-decoded-payload-name", true]]) {
    const client = new ManagementConfigurationClient({ baseUrl: "https://example.test/", transport: { async send(request) { return response(request, 200, sample(id)); } } });
    const result = await client.getRegistrationConfiguration(target);
    assert.equal(result.kind, "protocolFailure"); assert.equal(result.status, 200);
    assert.equal(result.contentRejection !== undefined, identified); assert.equal(result.contentRejection.desired.generation, "9007199254740993");
    assert.equal(result.value, undefined); assert.equal(result.problem, undefined);
  }
  for (const body of [{ ...snapshot, eTag: 'W/"weak"' }, { ...snapshot, desired: { ...snapshot.desired, authorityId: applicationId } }, { malformed: true }]) {
    const client = new ManagementConfigurationClient({ baseUrl: "https://example.test/", transport: { async send(request) { return response(request, 200, body); } } });
    assert.equal((await client.getRegistrationConfiguration(target)).contentRejection, undefined);
  }
});

test("304 reuses only complete same-session retained snapshot with exact ETag and never relaxes ordinary redirects", async () => {
  const retained = await decodeConfiguration("ConfigurationSnapshot", JSON.stringify(snapshot), "response");
  let calls = 0;
  const client = new ManagementConfigurationClient({ baseUrl: "https://example.test/", transport: { async send(request) {
    calls++; assert.equal(request.headers["If-None-Match"], retained.eTag); return response(request, 304, undefined, { etag: retained.eTag });
  } } });
  const unchanged = await client.getRegistrationConfiguration(target, retained);
  assert.equal(unchanged.kind, "http"); assert.equal(unchanged.status, 304); assert.deepEqual(unchanged.value, retained);
  assert.ok(Object.isFrozen(unchanged.value)); assert.equal(calls, 1);
  await assert.rejects(client.getRegistrationConfiguration({ ...target, cohortId: "foreign" }, retained), TypeError);
  assert.equal(calls, 1);
  for (const bad of [{ status: 304, headers: {} }, { status: 304, headers: { etag: '"another"' } }, { status: 302, headers: {} }]) {
    const strict = new ManagementConfigurationClient({ baseUrl: "https://example.test/", transport: { async send(request) { return response(request, bad.status, undefined, bad.headers); } } });
    assert.equal((await strict.getRegistrationConfiguration(target, retained)).kind, "protocolFailure");
  }
  const legacy = new ManagementClient({ baseUrl: "https://example.test/", transport: { async send(request) { return response(request, 304, undefined); } } });
  assert.equal((await legacy.getServiceInfo()).code, "http.redirect_blocked");
});

test("durable wrong-family and original-key echoes fail before return", async () => {
  for (const body of [{ ...original("publication"), operation: { ...original("publication").operation, key: "foreign" } }, original("assignment")]) {
    const client = new ManagementConfigurationClient({ baseUrl: "https://example.test/", transport: { async send(request) { return response(request, 200, body); } } });
    assert.equal((await client.publishSource(sourceId, key, ifMatch, sourceCommand)).kind, "protocolFailure");
  }
});

test("status receipt confirmation binds the exact detached submitted capture and malformed receipts remain expected failures", async () => {
  const report = sample("configuration-statusreport-valid");
  const sequence = report.sequence;
  const receipt = { registrationId: target.registrationId, stream: "configuration-status", submittedSequence: sequence,
    acceptedSequence: sequence, disposition: "accepted", receivedAtUtc: "2026-11-24T00:12:34.1234567Z" };
  const client = new ManagementConfigurationClient({ baseUrl: "https://example.test/", transport: { async send(request) {
    assert.equal(JSON.parse(new TextDecoder().decode(request.body)).sequence, sequence);
    report.sequence = "2";
    return response(request, 200, receipt);
  } } });
  assert.equal((await client.reportStatus(target.registrationId, report)).value.acceptedSequence, sequence);
  for (const invalid of [{ ...receipt, stream: "inventory" }, { ...receipt, acceptedSequence: "2" }, { incomplete: true }]) {
    const strict = new ManagementConfigurationClient({ baseUrl: "https://example.test/", transport: { async send(request) { return response(request, 200, invalid); } } });
    assert.equal((await strict.reportStatus(target.registrationId, sample("configuration-statusreport-valid"))).kind, "protocolFailure");
  }
});

function original(family) {
  const baseline = sample("configuration-operationresult-valid"); baseline.operation.key = key; baseline.operation.name = families[family];
  if (family === "catalog") { baseline.result = { commandVersion: 1, kind: "catalogApplied", decision: "applied", code: "configuration.catalog.applied",
    decidedAtUtc: baseline.result.decidedAtUtc, catalogId: "33333333-3333-3333-3333-333333333333", manifestDigest: "a".repeat(64), eTag: '"catalog-2"' }; }
  if (family === "publication") { baseline.result = { commandVersion: 1, kind: "sourcePublished", decision: "applied", code: "configuration.publication.applied",
    decidedAtUtc: baseline.result.decidedAtUtc, sourceId, publicationId: snapshot.commonSource.publicationId, sha256: snapshot.commonSource.sha256, eTag: '"source-2"' }; }
  if (family === "release") { baseline.result = { commandVersion: 1, kind: "releaseCreated", decision: "applied", code: "configuration.release.applied",
    decidedAtUtc: baseline.result.decidedAtUtc, applicationId, configurationId, schemaVersion: "1", releaseId: snapshot.desired.releaseId, eTag: '"release-1"' }; }
  return baseline;
}
function response(request, status, body, headers = {}) {
  return { status, url: request.url, redirected: false, headers: { "content-type": "application/json", ...headers },
    body: body === undefined ? null : new ReadableStream({ start(controller) { controller.enqueue(new TextEncoder().encode(JSON.stringify(body))); controller.close(); } }), async dispose() {} };
}
function problem(status) { return { type: "about:blank", title: "Unavailable", status, code: "configuration.unavailable" }; }
