import test from "node:test";
import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import {
  decodeManagement, encodeManagement, ManagementWireError, OpaqueJsonObject,
  knownOriginalCompletion, captureWireLimits
} from "@cislib/management-client";

const catalog = JSON.parse(await readFile(new URL("../../../conformance/management/v1/wire-cases.json", import.meta.url), "utf8"));
assert.equal(catalog.caseCount, catalog.cases.length);
for (const vector of catalog.cases) {
  test("portable wire: " + vector.id, () => {
    if (!vector.valid) {
      assert.throws(() => decodeManagement(vector.schema, vector.json, vector.direction), ManagementWireError);
      return;
    }
    const decoded = decodeManagement(vector.schema, vector.json, vector.direction);
    if (vector.expected !== undefined) { partialEqual(evidence(vector.schema, decoded), vector.expected); }
    assert.ok(Object.isFrozen(decoded));
    const roundTrip = decodeManagement(vector.schema, encodeManagement(vector.schema, decoded, vector.direction), vector.direction);
    assert.deepEqual(roundTrip, decoded);
  });
}

test("opaque module JSON retains unrecognized numbers without JavaScript rounding", () => {
  const raw = '{"sequence":"9007199254740993","observedAtUtc":"2026-11-23T12:34:56.1234567Z","modules":[{"kind":"future","version":"1","details":{"large":9007199254740993,"decimal":1.234567890123456789}}],"categories":[],"jobs":[]}';
  const value = decodeManagement("InventorySnapshot", raw, "request");
  assert.ok(value.modules[0].details instanceof OpaqueJsonObject);
  const encoded = new TextDecoder().decode(encodeManagement("InventorySnapshot", value, "request"));
  assert.ok(encoded.includes('"large":9007199254740993'));
  assert.ok(encoded.includes('"decimal":1.234567890123456789'));
  assert.equal(value.sequence, "9007199254740993");
});

test("unknown response facts remain raw and are explicitly unknown", () => {
  assert.deepEqual(knownOriginalCompletion({ originalCompletion: "futurePersistence" }), { value: "futurePersistence", isKnown: false });
  assert.deepEqual(knownOriginalCompletion({ originalCompletion: "unknown" }), { value: "unknown", isKnown: true });
});

test("native operation rejection preserves bounded original error without invented metadata", () => {
  const operation = { name: "metadata.replace", scope: "opaque-scope", key: "original-key" };
  const rejection = { code: "native rejection code", message: "Exact original\nmessage α" };
  const original = { operation, resultSource: "replayed", result: null, rejection, originalCompletion: "committed" };
  const decoded = decodeManagement("MetadataResult", JSON.stringify(original), "response");
  assert.equal(decoded.result, null); assert.deepEqual(decoded.rejection, rejection);
  const withoutNull = { ...original }; delete withoutNull.result;
  assert.deepEqual(decodeManagement("MetadataResult", JSON.stringify(withoutNull), "response").rejection, rejection);
  for (const message of ["\0", "\u0085", "α".repeat(513)]) {
    assert.throws(() => decodeManagement("MetadataResult", JSON.stringify({ ...original, rejection: { code: rejection.code, message } }), "response"), ManagementWireError);
  }
  assert.throws(() => decodeManagement("MetadataResult", JSON.stringify({ ...original, rejection: null }), "response"), ManagementWireError);
});

test("known integer tokens reject decimal or exponent forms without touching opaque JSON", () => {
  for (const value of ["1.0", "1e0"]) {
    assert.throws(() => decodeManagement("MetadataCommand", '{"commandVersion":' + value + ',"displayName":"One"}', "request"), ManagementWireError);
  }
  const inventory = '{"sequence":"1","observedAtUtc":"2026-11-23T12:34:56.1234567Z","modules":[{"kind":"future","version":"1","details":{"commandVersion":1e0,"version":1.0}}],"categories":[],"jobs":[]}';
  assert.ok(decodeManagement("InventorySnapshot", inventory, "request").modules[0].details.rawJson.includes("1e0"));
});

test("runtime wire enforces bytes, nesting, malformed UTF-8 and UTF-16", () => {
  const command = { commandVersion: 1, displayName: "α".repeat(256) };
  assert.throws(() => encodeManagement("MetadataCommand", command, "request", captureWireLimits({ maximumRegularRequestBytes: 100 })), ManagementWireError);
  assert.throws(() => decodeManagement("MetadataCommand", new Uint8Array([0xff]), "request"), ManagementWireError);
  assert.throws(() => encodeManagement("MetadataCommand", { commandVersion: 1, displayName: "\ud800" }, "request"), ManagementWireError);
  const details = { child: { child: { child: { child: {} } } } };
  const inventory = { sequence: "1", observedAtUtc: "2026-11-23T12:34:56.1234567Z", modules: [{ kind: "future", version: "1", details }], categories: [], jobs: [] };
  assert.throws(() => encodeManagement("InventorySnapshot", inventory, "request", captureWireLimits({ maximumJsonDepth: 4 })), ManagementWireError);
  assert.throws(() => captureWireLimits({ maximumJsonDepth: 33 }), RangeError);
});

function evidence(schema, value) {
  if (schema === "InventorySnapshot") {
    const module = value.modules[0]; const job = value.jobs[0]; const owner = job?.observations?.[0]?.observedOwner;
    return { ...value, moduleKind: module?.kind, largeIdentifier: module?.details === undefined ? undefined : JSON.parse(module.details.rawJson).opaque?.largeIdentifier,
      configurationRevision: job?.effectiveConfiguration?.revision, generation: owner?.generation, ownerObservedAtUtc: owner?.observedAtUtc,
      categoryDescription: value.categories[0]?.description, jobDescription: job?.description, resourceScopeDescription: job?.resourceScopeDescription };
  }
  if (schema === "MetadataCommand" || schema === "Application") {
    const metadata = schema === "Application" ? value.metadata : value;
    return { ...value, ...metadata, descriptionAbsent: !Object.hasOwn(metadata, "description"),
      displayNameScalars: [...metadata.displayName].length, descriptionScalars: metadata.description === undefined ? undefined : [...metadata.description].length };
  }
  if (schema === "MetadataResult") { return { ...value, ...value.result, ...value.rejection,
    decision: value.rejection == null ? value.result.decision : "rejected", rejectionCode: value.rejection?.code,
    rejectionMessage: value.rejection?.message, knownOriginalCompletion: knownOriginalCompletion(value).isKnown,
    attemptOutcome: value.attemptCompletion?.outcome, cleanupSucceeded: value.attemptCompletion?.cleanupSucceeded }; }
  if (schema === "Problem") { return { ...value, key: value.operation?.key, originalCompletion: value.result?.originalCompletion }; }
  return value;
}

function partialEqual(actual, expected) {
  for (const [key, value] of Object.entries(expected)) {
    if (value !== null && typeof value === "object" && !Array.isArray(value)) { partialEqual(actual[key], value); }
    else { assert.deepEqual(actual[key], value, key); }
  }
}
