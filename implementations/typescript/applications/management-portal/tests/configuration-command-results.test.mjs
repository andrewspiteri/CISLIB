import assert from "node:assert/strict";
import test from "node:test";
import { projectConfigurationCommand, resolvedConfigurationOriginal } from "../src/configuration-command-results.ts";

const app = "11111111-1111-4111-8111-111111111111";
const source = "22222222-2222-4222-8222-222222222222";
const binding = "33333333-3333-4333-8333-333333333333";
const key = "44444444-4444-4444-8444-444444444444";
const publication = "55555555-5555-4555-8555-555555555555";
const publicationIntent = { family: "management.configuration.source.publish", target: { sourceId: source } };
const releaseIntent = { family: "management.configuration.release.create", target: { applicationId: app, configurationId: "settings", schemaVersion: "9007199254740993" } };
function original(intent = publicationIntent) {
  const publishing = intent.family === publicationIntent.family;
  return { operation: { name: intent.family, scope: `management.configuration.v1/${intent.family}/${publishing ? source : binding}/management.actor.v1.${"a".repeat(64)}`, key },
    resultSource: "found", originalCompletion: "committed", attemptCompletion: { outcome: "notCommitted", cleanupSucceeded: true },
    result: { commandVersion: 1, kind: publishing ? "sourcePublished" : "releaseCreated", decision: "applied", code: "management.configuration.applied",
      decidedAtUtc: "2026-11-26T11:00:00.1234567Z", ...(publishing ? { sourceId: source, publicationId: publication, sha256: "b".repeat(64), eTag: '"source-after"' }
        : { ...intent.target, releaseId: publication, eTag: '"release-etag"' }) } };
}
function call(value) { return { kind: "http", status: 200, value, attempts: 1, durationMs: 1, cleanupSucceeded: true, mutationUncertain: false }; }

test("creation original stays app-scoped and payload-free while retaining a known commit through cleanup failure", () => {
  const intent = { family: "management.configuration.create", target: { applicationId: app } };
  const value = { operation: { name: intent.family, scope: `management.configuration.v1/${intent.family}/${app}/management.actor.v1.${"a".repeat(64)}`, key },
    resultSource: "found", originalCompletion: "committed", result: { commandVersion: 1, kind: "configurationCreated", decision: "applied",
      code: "management.configuration.applied", decidedAtUtc: "2026-11-26T11:00:00.1234567Z", applicationId: app, configurationId: "new.settings", schemaVersion: "1",
      sourceId: source, bindingId: binding, hostApplicationName: "NewHost", sourceETag: '"source-0"', eTag: '"creation-1"', payloadJson: '{"private":true}', schema: { private: true } } };
  const projected = projectConfigurationCommand({ ...call(value), kind: "transportFailure", cleanupSucceeded: false, mutationUncertain: true }, intent, key);
  assert.equal(projected.disposition, "applied"); assert.equal(resolvedConfigurationOriginal(projected), true);
  assert.equal(projected.decision.sourceId, source); assert.equal(projected.transport.cleanupSucceeded, false);
  assert.equal(JSON.stringify(projected).includes("private"), false);
  for (const mutate of [candidate => { candidate.result.applicationId = source; }, candidate => { candidate.operation.scope = candidate.operation.scope.replace(app, source); },
    candidate => { candidate.result.sourceETag = "not-an-etag"; }, candidate => { candidate.result.schemaVersion = "2"; }, candidate => { candidate.result.kind = "sourcePublished"; }]) {
    const candidate = structuredClone(value); mutate(candidate);
    assert.equal(projectConfigurationCommand(call(candidate), intent, key).disposition, "unresolved");
  }
});

test("source and release original commits survive current transport and read-cleanup faults independently", () => {
  for (const intent of [publicationIntent, releaseIntent]) {
    const result = projectConfigurationCommand({ ...call(original(intent)), kind: "transportFailure", cleanupSucceeded: false, mutationUncertain: true }, intent, key);
    assert.equal(result.disposition, "applied"); assert.equal(result.originalCompletion, "committed");
    assert.equal(result.attemptCompletion.outcome, "notCommitted"); assert.equal(result.transport.cleanupSucceeded, false);
    assert.equal(resolvedConfigurationOriginal(result), true);
  }
});

test("unknown missing denied expired and future observations retain the original key without replacement permission", () => {
  for (const status of [403, 404, 410, 412, 503]) {
    const result = projectConfigurationCommand({ ...call(), status, problem: { code: "management.unavailable", status } }, publicationIntent, key);
    assert.equal(result.disposition, "unresolved"); assert.equal(result.key, key); assert.equal(result.decision, undefined);
  }
  for (const edit of [value => { value.originalCompletion = "unknown"; }, value => { value.resultSource = "future"; },
    value => { value.result.kind = "future"; }]) {
    const value = original(); edit(value);
    assert.equal(projectConfigurationCommand(call(value), publicationIntent, key).disposition, "unresolved");
  }
  for (const code of ["policy.refused", "management.configuration.precondition_failed"]) {
    const value = original(); value.result.kind = "future"; value.result.decision = "rejected"; value.result.code = code;
    const result = projectConfigurationCommand(call(value), publicationIntent, key);
    assert.equal(result.disposition, "unresolved"); assert.equal(resolvedConfigurationOriginal(result), false);
  }
});

test("known conflict rejection and not-committed facts remain deliberate resolved observations", () => {
  const conflict = original(); delete conflict.result;
  conflict.rejection = { code: "management.configuration.precondition_failed", message: "Changed source." };
  assert.equal(projectConfigurationCommand(call(conflict), publicationIntent, key).disposition, "conflict");
  conflict.rejection.code = "policy.refused";
  assert.equal(projectConfigurationCommand(call(conflict), publicationIntent, key).disposition, "rejected");
  conflict.originalCompletion = "notCommitted";
  const result = projectConfigurationCommand(call(conflict), publicationIntent, key);
  assert.equal(result.disposition, "notCommitted"); assert.equal(resolvedConfigurationOriginal(result), true);
});

test("foreign family key resource or any available decision target cannot cross the result boundary", () => {
  for (const edit of [value => { value.operation.name = releaseIntent.family; }, value => { value.operation.key = app; },
    value => { value.operation.scope = value.operation.scope.replace(source, app); }, value => { value.operation.scope += "/suffix"; },
    value => { value.result.sourceId = app; }]) {
    const value = original(); edit(value);
    const result = projectConfigurationCommand(call(value), publicationIntent, key);
    assert.equal(result.code, "portal.operation.target_mismatch"); assert.equal(result.decision, undefined);
  }
  for (const [field, value] of [["applicationId", source], ["configurationId", "other"], ["schemaVersion", "1"]]) {
    const native = original(releaseIntent); native.result.kind = "future"; native.result[field] = value;
    assert.equal(projectConfigurationCommand(call(native), releaseIntent, key).code, "portal.operation.target_mismatch");
  }
});

test("only the selected family facts are copied and frozen; native additions do not become browser data", () => {
  const value = original(); value.result.secret = "hidden"; value.result.catalogId = app; value.operation.secret = "hidden";
  const result = projectConfigurationCommand(call(value), publicationIntent, key);
  assert.equal(result.decision.secret, undefined); assert.equal(result.decision.catalogId, undefined); assert.equal(result.operation.secret, undefined);
  value.result.publicationId = app; value.attemptCompletion.cleanupSucceeded = false;
  assert.equal(result.decision.publicationId, publication); assert.equal(result.attemptCompletion.cleanupSucceeded, true);
  assert.ok(Object.isFrozen(result.decision)); assert.ok(Object.isFrozen(result.target));
});
