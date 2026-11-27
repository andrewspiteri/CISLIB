import assert from "node:assert/strict";
import test from "node:test";
import { projectConfigurationActions, projectConfigurationCohortPage, projectConfigurationReleasePage } from "../src/configuration-editor-model.ts";
import { projectConfigurationCommand } from "../src/configuration-command-results.ts";

const applicationId = "11111111-1111-4111-8111-111111111111";
const bindingId = "22222222-2222-4222-8222-222222222222";
const releaseId = "33333333-3333-4333-8333-333333333333";
const foreignId = "44444444-4444-4444-8444-444444444444";
const key = "55555555-5555-4555-8555-555555555555";
const target = { applicationId, configurationId: "country.settings", schemaVersion: "9007199254740993" };
const intent = { family: "management.configuration.assignment.replace", target };

test("retained release selection discloses allowlisted metadata and exact references, never compatible future payloads", () => {
  const release = { ...target, releaseId, environment: "staging", commonSource: { state: "absent", payloadJson: "private" },
    applicationSource: { state: "present", publicationId: foreignId, payloadJson: "private" }, actor: "operator",
    createdAtUtc: "2026-11-26T11:00:00.1234567Z", reason: "Exact e\u0301 reason", eTag: '"release.1"', payloadJson: "private" };
  const page = projectConfigurationReleasePage({ items: [release], nextCursor: "opaque", privateContent: "private" });
  assert.equal(page.privateContent, undefined);
  assert.equal(page.items[0].payloadJson, undefined);
  assert.deepEqual(page.items[0].commonSource, { state: "absent" });
  assert.deepEqual(page.items[0].applicationSource, { state: "present", publicationId: foreignId });
  assert.equal(page.items[0].schemaVersion, "9007199254740993");
  assert.equal(page.items[0].reason, "Exact e\u0301 reason");
  assert.equal(page.nextCursor, "opaque");
  release.reason = "changed later";
  assert.equal(page.items[0].reason, "Exact e\u0301 reason");
  assert.ok(Object.isFrozen(page.items[0].applicationSource));
});

test("cohort discovery exposes only native choice names and opaque IDs, without principal membership extensions", () => {
  const input = { items: [{ cohortId: "finance", displayName: "Finance", principals: ["private"], grant: "private" }], principalMappings: "private" };
  const page = projectConfigurationCohortPage(input);
  assert.deepEqual(page, { items: [{ cohortId: "finance", displayName: "Finance" }] });
  input.items[0].displayName = "changed later";
  assert.equal(page.items[0].displayName, "Finance");
  assert.ok(Object.isFrozen(page.items));
});

test("current action projection preserves support and authority as separate hints without extra grants", () => {
  const result = projectConfigurationActions({ resourceId: bindingId, grant: "private",
    actions: [{ operationId: "replaceConfigurationAssignments", supported: true, allowed: false, reasonCode: "denied", credential: "private" }] });
  assert.deepEqual(result, { resourceId: bindingId,
    actions: [{ operationId: "replaceConfigurationAssignments", supported: true, allowed: false, reasonCode: "denied" }] });
  assert.ok(Object.isFrozen(result.actions[0]));
});

function original() {
  const assignment = { ...target, bindingId, environment: "staging", eTag: '"map.after"', generation: "9007199254740993",
    defaultReleaseId: releaseId, overrides: [{ cohortId: "finance", releaseId: foreignId }], payloadJson: "private" };
  return { operation: { name: intent.family, key,
    scope: `management.configuration.v1/${intent.family}/${bindingId}/management.actor.v1.${"a".repeat(64)}` },
    resultSource: "found", originalCompletion: "committed", attemptCompletion: { outcome: "notCommitted", cleanupSucceeded: false },
    result: { ...target, commandVersion: 1, kind: "assignmentsReplaced", decision: "applied", code: "management.configuration.assignments_replaced",
      decidedAtUtc: "2026-11-26T11:00:00.1234567Z", generation: assignment.generation, eTag: assignment.eTag, intent: "rollback", assignment } };
}
function call(value) { return { kind: "transportFailure", value, attempts: 1, durationMs: 1, cleanupSucceeded: false, mutationUncertain: true }; }

test("known rollback commitment survives the lookup cleanup fault and retains every exact map pin", () => {
  const result = projectConfigurationCommand(call(original()), intent, key);
  assert.equal(result.disposition, "applied");
  assert.equal(result.originalCompletion, "committed");
  assert.equal(result.attemptCompletion.outcome, "notCommitted");
  assert.equal(result.decision.generation, "9007199254740993");
  assert.equal(result.decision.intent, "rollback");
  assert.deepEqual(result.decision.assignment.overrides, [{ cohortId: "finance", releaseId: foreignId }]);
  assert.equal(result.decision.assignment.payloadJson, undefined);
});

test("assignment observations refuse a foreign returned map even for a native rejection", () => {
  for (const decision of ["applied", "rejected"]) {
    for (const field of ["bindingId", "applicationId", "configurationId", "schemaVersion"]) {
      const value = original(); value.result.decision = decision;
      value.result.assignment[field] = field === "configurationId" ? "other.settings" : field === "schemaVersion" ? "2" : foreignId;
      const result = projectConfigurationCommand(call(value), intent, key);
      assert.equal(result.disposition, "unresolved");
      assert.equal(result.code, "portal.operation.target_mismatch");
      assert.equal(result.decision, undefined);
    }
  }
});
