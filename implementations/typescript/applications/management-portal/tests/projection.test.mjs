import assert from "node:assert/strict";
import test from "node:test";
import { projectCapabilities, projectDefinition, projectInventory } from "../src/server/projection.ts";

test("configuration inspection omits defaults and schema values while retaining exact source identity", () => {
  const projected = projectDefinition({ kind: "application", configurationId: "settings", schemaVersion: "1", sourceId: "source",
    environment: "staging", applicationId: "app", bindingId: "binding", hostApplicationName: "Demo.CountryData",
    description: "Description", defaultsJson: '{"private":"not-for-the-portal"}',
    shape: { kind: "object", nullable: false, members: [{ name: "secret", required: true,
      shape: { kind: "string", nullable: false, allowedValues: ["not-for-the-portal"] } }] } });
  assert.equal(projected.sourceId, "source");
  assert.equal(projected.memberCount, 1);
  assert.equal(projected.kind, "application");
  assert.equal(JSON.stringify(projected).includes("not-for-the-portal"), false);
  assert.throws(() => projectDefinition({ kind: "common" }));
});

test("capabilities preserve actual APIs while absent hints remain unevaluated", () => {
  const value = projectCapabilities({ applicationId: "app", implementedApis: [
    { operationId: "read", version: 1, method: "GET", path: "/read", requiredCapabilities: ["read"] },
    { operationId: "write", version: 1, method: "PUT", path: "/write", requiredCapabilities: ["write"] }
  ], callerHints: [{ operationId: "read", allowed: false }] });
  assert.equal(value.implementedApis[0].callerHint, "denied");
  assert.equal(value.implementedApis[1].callerHint, "notEvaluated");
  assert.equal(value.implementedApis[1].method, "PUT");
});

test("inventory keeps the whole scoped report and unknown observations but excludes arbitrary modules", () => {
  const value = projectInventory({ registrationId: "registration", applicationVersion: "2.4.2+build.18", receivedAtUtc: "receipt",
    inventory: { sequence: "9007199254740993", observedAtUtc: "observed", modules: [{ kind: "future", version: "1", details: { private: "not-for-the-portal" } }],
      categories: [{ executionScope: "scope", categoryId: "category", displayName: "Category" }],
      jobs: [{ executionScope: "scope", jobId: "job", categoryId: "category", displayName: "Job", supportedModes: ["scheduled"],
        effectiveConfiguration: { revision: "0", enabled: false }, observations: [{ resourceId: "resource", mode: "future", state: "unknown-state" }] }] } });
  assert.equal(value.inventory.sequence, "9007199254740993");
  assert.equal(value.applicationVersion, "2.4.2+build.18");
  assert.equal(value.inventory.jobs[0].effectiveConfiguration.enabled, false);
  assert.equal(value.inventory.jobs[0].observations[0].state, "unknown-state");
  assert.equal(JSON.stringify(value).includes("not-for-the-portal"), false);
  assert.equal("modules" in value.inventory, false);
});
