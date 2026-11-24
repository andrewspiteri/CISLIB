import test from "node:test";
import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import {
  decodeConfiguration, encodeConfiguration, decodeConfigurationSnapshot, computeConfigurationPayloadSha256,
  ConfigurationWireError, captureConfigurationWireLimits, knownConfigurationState, knownConfigurationConvergence,
  knownConfigurationIntent
} from "@cislib/management-client";

const catalog = JSON.parse(await readFile(new URL("../../../conformance/management/v1/configuration-cases.json", import.meta.url), "utf8"));
assert.equal(catalog.caseCount, catalog.cases.length);
for (const vector of catalog.cases) {
  test("portable configuration wire: " + vector.id, async () => {
    if (!vector.valid) {
      await assert.rejects(decodeConfiguration(vector.schema, vector.json, vector.direction), ConfigurationWireError);
    } else {
      const value = await decodeConfiguration(vector.schema, vector.json, vector.direction);
      assert.ok(Object.isFrozen(value));
      const roundTrip = await decodeConfiguration(vector.schema, await encodeConfiguration(vector.schema, value, vector.direction), vector.direction);
      assert.deepEqual(roundTrip, value);
    }
    if (vector.schema === "ConfigurationSnapshot") {
      const read = await decodeConfigurationSnapshot(vector.json, target());
      assert.equal(read.snapshot !== undefined, vector.valid);
      if (vector.expected?.identifiedDesired !== undefined) { assert.equal(read.desired !== undefined, vector.expected.identifiedDesired); }
      if (vector.expected?.code !== undefined) { assert.equal(read.failureCode, vector.expected.code); }
    }
  });
}

test("exact Unicode payload digest never normalizes or reserializes source JSON", async () => {
  const payload = '{"Greeting":"Café — 日本語","Enabled":false}';
  const digest = "6b22cc1785a4a64b26ce415be9c4a3cfbe21aa5b7127644f87004d92192429d9";
  assert.equal(await computeConfigurationPayloadSha256(payload), digest);
  assert.notEqual(await computeConfigurationPayloadSha256(payload + " "), digest);
  assert.notEqual(await computeConfigurationPayloadSha256('{"text":"é"}'), await computeConfigurationPayloadSha256('{"text":"e\u0301"}'));
  for (const invalid of ["\ufeff{}", '{"x":1,"\\u0078":2}', '{"x":"\ud800"}', "[]", "null"]) {
    await assert.rejects(computeConfigurationPayloadSha256(invalid), ConfigurationWireError);
  }
});

test("outer identity and ETag must be valid before content can identify rejected desired state", async () => {
  const baseline = snapshot();
  for (const mutate of [value => { value.desired.authorityId = value.desired.applicationId; },
    value => { value.desired.generation = "01"; }, value => { value.eTag = 'W/"weak"'; }, value => { delete value.applicationSource; }]) {
    const value = structuredClone(baseline); mutate(value);
    const read = await decodeConfigurationSnapshot(JSON.stringify(value), target());
    assert.equal(read.snapshot, undefined); assert.equal(read.desired, undefined);
  }
  for (const input of [new Uint8Array([255]), new Uint8Array([239, 187, 191, 123, 125]), "{broken"]) {
    assert.equal((await decodeConfigurationSnapshot(input, target())).desired, undefined);
  }
  for (const field of ["schemaVersion", "environment", "kind"]) {
    const value = structuredClone(baseline); value.commonSource[field] = field === "schemaVersion" ? "2" : field === "kind" ? "application" : "Production";
    const read = await decodeConfigurationSnapshot(JSON.stringify(value), target());
    assert.equal(read.snapshot, undefined); assert.equal(read.desired.releaseId, baseline.desired.releaseId);
  }
});

test("whole encoded envelopes and decoded sources have independent bounds", async () => {
  const value = snapshot();
  await assert.rejects(decodeConfiguration("ConfigurationSnapshot", JSON.stringify(value), "response", { maximumBodyBytes: 64 }), ConfigurationWireError);
  await assert.rejects(computeConfigurationPayloadSha256('{"x":"' + "é".repeat(150000) + '"}'), ConfigurationWireError);
  const payload = '{"x":"' + "\\\\".repeat(131000) + '"}';
  const sha256 = await computeConfigurationPayloadSha256(payload);
  value.commonSource = { ...value.commonSource, payloadJson: payload, sha256 };
  value.applicationSource = { ...value.commonSource, kind: "application", applicationId: value.desired.applicationId };
  await assert.rejects(encodeConfiguration("ConfigurationSnapshot", value, "response"), ConfigurationWireError);
  assert.throws(() => captureConfigurationWireLimits({ maximumSourceBytes: 262145 }), RangeError);
});

test("counts use exact bounded integer tokens while source JSON retains its original number text", async () => {
  const current = catalog.cases.find(row => row.id === "configuration-catalog-valid").json;
  for (const token of ["1.0", "1e0", "2147483648"]) {
    const invalid = current.replace(/"sourceCount":1/u, '"sourceCount":' + token);
    await assert.rejects(decodeConfiguration("ConfigurationCatalog", invalid, "response"), ConfigurationWireError);
  }
  const publication = JSON.parse(catalog.cases.find(row => row.id === "configuration-publication-valid").json);
  publication.payloadJson = '{"large":9007199254740993,"fraction":1.234567890123456789}';
  publication.sha256 = await computeConfigurationPayloadSha256(publication.payloadJson);
  const decoded = await decodeConfiguration("ConfigurationPublication", JSON.stringify(publication), "response");
  assert.equal(decoded.payloadJson, publication.payloadJson);
});

test("future states and intent remain explicit unknowns and response additions stay additive", async () => {
  assert.deepEqual(knownConfigurationState("FutureState"), { value: "FutureState", isKnown: false });
  assert.equal(knownConfigurationConvergence("future-convergence").isKnown, false);
  assert.equal(knownConfigurationIntent("future-intent").isKnown, false);
  const value = snapshot(); value.future = { status: 1e0 }; value.desired.future = null;
  assert.ok(await decodeConfiguration("ConfigurationSnapshot", JSON.stringify(value), "response"));
  const command = { commandVersion: 1, reason: "Publish", payloadJson: "{}", unexpected: true };
  await assert.rejects(encodeConfiguration("ConfigurationPublicationCommand", command, "request"), ConfigurationWireError);
});

function snapshot() { return JSON.parse(catalog.cases.find(row => row.id === "configuration-snapshot-valid").json); }
function target() { const { releaseId: _release, generation: _generation, ...expected } = snapshot().desired; return expected; }
