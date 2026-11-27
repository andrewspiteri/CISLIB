import assert from "node:assert/strict";
import test from "node:test";
import { isCompletedConfigurationDeployment } from "../src/configuration-deployment-receipts.ts";

const applicationId = "11111111-1111-4111-8111-111111111111", configurationId = "country.settings", schemaVersion = "1";
const key = "22222222-2222-4222-8222-222222222222", family = "management.configuration.assignment.replace";
const target = { applicationId, configurationId, schemaVersion };
const receipt = { family, target, key, operatorId: "a".repeat(64), serviceAssociation: "b".repeat(64), createdAtMs: 1800000000000 };
const known = { family, target, key, disposition: "applied", originalCompletion: "committed", resultSource: "prepared",
  operation: { name: family, key, scope: "management.configuration.binding.v1/native-binding/management.actor.v1." + "a".repeat(64) },
  decision: { commandVersion: 1, kind: "assignmentsReplaced", decision: "applied", code: "management.configuration.assigned", decidedAtUtc: "2026-11-26T20:00:00.0000000Z",
    applicationId, configurationId, schemaVersion, assignment: { ...target, bindingId: "33333333-3333-4333-8333-333333333333", environment: "Development", eTag: '"map-2"', generation: "2", defaultReleaseId: "44444444-4444-4444-8444-444444444444", overrides: [] } },
  transport: { kind: "http", status: 200, cleanupSucceeded: true, mutationUncertain: false } };

test("a displayed exact clean deployment can be acknowledged for a later deliberate Deploy, including rollback", () => {
  assert.equal(isCompletedConfigurationDeployment(receipt, known, applicationId, configurationId, schemaVersion), true);
  assert.equal(isCompletedConfigurationDeployment(receipt, { ...known, resultSource: "found", recovery: { state: "confirmed" } }, applicationId, configurationId, schemaVersion), true);
  assert.equal(receipt.key, key, "Eligibility does not mutate, dismiss or replace the retained original.");
});

test("unknown, refused, weaker recovery and independent cleanup failures never unblock a new deployment", () => {
  for (const value of [undefined, { ...known, disposition: "unresolved" }, { ...known, disposition: "rejected" },
    { ...known, originalCompletion: "unknown" }, { ...known, resultSource: "future" }, { ...known, transport: { ...known.transport, cleanupSucceeded: false } },
    { ...known, attemptCompletion: { outcome: "Committed", cleanupSucceeded: false } },
    { ...known, transport: { ...known.transport, mutationUncertain: true } }, { ...known, recovery: { state: "retained" } },
    { ...known, recovery: { state: "conflicting" } }, { ...known, decision: { ...known.decision, kind: "futureDecision" } }]) {
    assert.equal(isCompletedConfigurationDeployment(receipt, value, applicationId, configurationId, schemaVersion), false);
  }
});

test("the key alone cannot acknowledge another family, target, original or nested resulting map", () => {
  const foreign = "55555555-5555-4555-8555-555555555555";
  for (const value of [{ ...known, key: foreign }, { ...known, family: "management.configuration.release.create" },
    { ...known, target: { ...target, applicationId: foreign } }, { ...known, target: { ...target, schemaVersion: "2" } },
    { ...known, operation: { ...known.operation, key: foreign } }, { ...known, operation: { ...known.operation, name: "foreign.operation" } },
    { ...known, decision: { ...known.decision, assignment: { ...known.decision.assignment, configurationId: "other.settings" } } }]) {
    assert.equal(isCompletedConfigurationDeployment(receipt, value, applicationId, configurationId, schemaVersion), false);
  }
  assert.equal(isCompletedConfigurationDeployment({ ...receipt, target: { ...target, configurationId: "other.settings" } }, known, applicationId, configurationId, schemaVersion), false);
});
