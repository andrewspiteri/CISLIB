import assert from "node:assert/strict";
import test from "node:test";
import { inspectExecution } from "../src/rule-engine-history-model.ts";
import { readRuleExecutionFilters, ruleEngineRoute } from "../src/rule-engine-routes.ts";
import { projectRuleOperation } from "../src/rule-engine-operations.ts";

const applicationId = "11111111-1111-4111-8111-111111111111", executionId = "22222222-2222-4222-8222-222222222222";
const version = "33333333-3333-4333-8333-333333333333", rulesetId = "eligibility";
const scope = { applicationId, rulesetId, environment: "Development" };
const time = "2026-11-27T21:16:03.9999999Z";
const definitionJson = '{"rulesetId":"' + rulesetId + '","schemaRevision":"schema","version":"' + version
  + '","mode":"allMatching","columns":[{"id":"income","displayName":"Income","inputKind":"decimal","allowedPresetKinds":["decimal"]},'
  + '{"id":"referral","displayName":"Referral","inputKind":"string","allowedPresetKinds":["string"]}],'
  + '"rows":[{"ruleId":"r1","ordinal":10,"state":"test","weight":"0.2","conditions":[{"columnId":"income","presetKind":"any"},{"columnId":"referral","presetKind":"missing"}]},'
  + '{"ruleId":"r2","ordinal":20,"state":"enabled","weight":"1.2500","metadata":{"discount":9007199254740993},'
  + '"conditions":[{"columnId":"income","presetKind":"any"},{"columnId":"referral","presetKind":"missing"}]}]}';
const startAuditJson = '{"details":{"engineContractVersion":1,"correlationId":"correlation","inputs":[{"columnId":"income","kind":"decimal","value":"9007199254740993.0001"},'
  + '{"columnId":"comment","kind":"null"},{"columnId":"extras","kind":"json","value":{"rate":1.2300e5}}]}}';
const completionAuditJson = '{"details":{"engineContractVersion":1,"correlationId":"correlation","status":"Completed",'
  + '"rows":[{"rowIndex":0,"state":"Test","outcome":"Matched","cellOutcomes":"mm"},{"rowIndex":1,"state":"Enabled","outcome":"Matched","cellOutcomes":"mu"}],'
  + '"selectedRowIndices":[1]}}';
const summary = { ...scope, executionId, version, schemaRevision: "schema", status: "Completed", startReceived: true, completionReceived: true };

test("protected audit projection preserves typed exact supply semantics and observed return order", () => {
  const view = inspectExecution({ execution: summary, definitionJson, startAuditJson, completionAuditJson });
  assert.deepEqual(view.inputs.map(item => [item.columnId, item.presence, item.value, item.unknown]), [
    ["income", "Supplied", "9007199254740993.0001", false], ["referral", "Omitted", undefined, false],
    ["comment", "Supplied null", "null", true], ["extras", "Supplied", '{"rate":1.2300e5}', true]
  ]);
  assert.equal(view.trace[0].outcome, "Matched · test hit");
  assert.deepEqual(view.trace[1].cells, ["Matched", "Not evaluated"]);
  assert.deepEqual(view.selected.map(row => [row.ruleId, row.weight, row.metadataText]), [["r2", "1.2500", '{"discount":9007199254740993}']]);
  assert.equal(view.correlationId, "correlation");
});
test("incomplete audit retains only observed phases and withholds terminal results", () => {
  const view = inspectExecution({ execution: { ...summary, status: "Incomplete", startReceived: false }, definitionJson, completionAuditJson });
  assert.equal(view.inputs, undefined);
  assert.equal(view.selected, undefined);
  assert.equal(view.trace.length, 2);
  const empty = inspectExecution({ execution: summary, definitionJson, startAuditJson,
    completionAuditJson: completionAuditJson.replace('"selectedRowIndices":[1]', '"selectedRowIndices":[]') });
  assert.deepEqual(empty.selected, []);
});
test("exact history return route retains filters, scope, cursor and focus without creating authority", () => {
  const filters = readRuleExecutionFilters(key => ({ executionId, version, startedFromUtc: time, startedThroughUtc: time, status: "Completed" })[key]);
  const route = new URL(ruleEngineRoute({ ...scope, view: "execution", executionId, history: { filters, cursor: "signed.token", focusExecution: executionId } }),
    "https://portal.example");
  assert.ok(route.pathname.endsWith("/executions/" + executionId));
  assert.equal(route.searchParams.get("historyCursor"), "signed.token");
  assert.equal(route.searchParams.get("startedFromUtc"), time);
  assert.equal(route.searchParams.get("focusExecution"), executionId);
  assert.throws(() => readRuleExecutionFilters(key => key === "startedFromUtc" ? "2026-11-27T21:16:04.0000000Z" : key === "startedThroughUtc" ? time : undefined));
});
test("counter reset outcome preserves native zero and partial rejection counts", () => {
  const family = "management.rule-engine.counter.reset", target = { applicationId, rulesetId };
  const result = projectRuleOperation({ cleanupSucceeded: true, value: { ...scope,
    operation: { name: family, key: "original" }, originalCompletion: "committed", resultSource: "found",
    result: { selection: "selected", resetAtUtc: time, appliedCount: "0", rejectedCount: "2" } } }, family, target, "original");
  assert.equal(result.disposition, "committed");
  assert.deepEqual(result.counterReset, { selection: "selected", resetAtUtc: time, appliedCount: "0", rejectedCount: "2" });
});
