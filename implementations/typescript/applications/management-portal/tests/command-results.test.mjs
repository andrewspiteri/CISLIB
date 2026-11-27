import test from "node:test";
import assert from "node:assert/strict";
import { projectMetadataCommand } from "../src/command-results.ts";

const app = "11111111-1111-4111-8111-111111111111";
const other = "22222222-2222-4222-8222-222222222222";
const key = "33333333-3333-4333-8333-333333333333";
function original() {
  return { operation: { name: "management.metadata.replace", scope: `management.metadata.v1/${app}/management.actor.v1.${"a".repeat(64)}`, key },
    resultSource: "prepared", originalCompletion: "committed", attemptCompletion: { outcome: "committed", cleanupSucceeded: true },
    result: { commandVersion: 1, applicationId: app, decision: "applied", code: "management.metadata.applied",
      metadata: { displayName: "Café 日本語", description: "" }, metadataRevision: "9007199254740993", metadataETag: '"meta-2"',
      decidedAtUtc: "2026-11-26T09:00:00.1234567Z" } };
}
function call(value = original()) {
  return { kind: "http", status: 200, value, attempts: 1, durationMs: 10, cleanupSucceeded: true, mutationUncertain: false, operationKey: key };
}

test("a committed decision survives native and transport cleanup failure on an HTTP problem", () => {
  const value = original(); value.attemptCompletion = { outcome: "unknown", cleanupSucceeded: false, cleanupCodes: ["cleanup.failed"] };
  const input = { ...call(), value: undefined, status: 503, cleanupSucceeded: false, mutationUncertain: true,
    problem: { status: 503, code: "management.operation.cleanup_failed", result: value } };
  const result = projectMetadataCommand(input, app, key);
  assert.equal(result.disposition, "applied"); assert.equal(result.originalCompletion, "committed");
  assert.equal(result.transport.status, 503); assert.equal(result.transport.cleanupSucceeded, false);
  assert.equal(result.attemptCompletion.cleanupSucceeded, false); assert.equal(result.key, key);
  assert.equal(result.decision.metadataRevision, "9007199254740993");
});

test("a fresh result lookup rollback belongs to the read rather than the original effect", () => {
  const value = original(); value.resultSource = "found"; value.attemptCompletion = { outcome: "notCommitted", cleanupSucceeded: true };
  const result = projectMetadataCommand(call(value), app, key);
  assert.equal(result.disposition, "applied"); assert.equal(result.originalCompletion, "committed");
  assert.equal(result.attemptCompletion.outcome, "notCommitted");
});

test("original conflicts and rejections remain separate from unavailable or missing lookups", () => {
  const value = original(); value.result.decision = "rejected"; value.result.code = "management.precondition_failed";
  assert.equal(projectMetadataCommand(call(value), app, key).disposition, "conflict");
  delete value.result; value.rejection = { code: "policy.refused", message: "The original change was refused." };
  assert.equal(projectMetadataCommand(call(value), app, key).disposition, "rejected");
  for (const status of [403, 404, 410, 412, 503]) {
    const result = projectMetadataCommand({ ...call(), value: undefined, status, problem: { status, code: "management.unavailable" } }, app, key);
    assert.equal(result.disposition, "unresolved"); assert.equal(result.key, key); assert.equal(result.decision, undefined);
  }
});

test("prepared proposed decisions and future facts never become a completed business effect", () => {
  for (const [field, marker, expected] of [["originalCompletion", "unknown", "unresolved"], ["originalCompletion", "notCommitted", "notCommitted"],
    ["originalCompletion", "future", "unresolved"], ["resultSource", "future", "unresolved"]]) {
    const value = original(); value[field] = marker;
    const result = projectMetadataCommand(call(value), app, key);
    assert.equal(result.disposition, expected); assert.equal(result.originalCompletion, value.originalCompletion);
  }
  const value = original(); value.result.decision = "future";
  assert.equal(projectMetadataCommand(call(value), app, key).disposition, "unresolved");
});

test("foreign native family key scope or decision target cannot cross the private result projection", () => {
  const edits = [value => { value.operation.name = "other.operation"; }, value => { value.operation.key = other; },
    value => { value.operation.scope = `management.metadata.v1/${other}/management.actor.v1.${"a".repeat(64)}`; },
    value => { value.operation.scope += "/suffix"; }, value => { value.result.applicationId = other; }];
  for (const edit of edits) {
    const value = original(); edit(value); const result = projectMetadataCommand(call(value), app, key);
    assert.equal(result.disposition, "unresolved"); assert.equal(result.code, "portal.operation.target_mismatch");
    assert.equal(result.decision, undefined); assert.equal(result.operation, undefined); assert.equal(result.key, key);
  }
});

test("private command projection copies only approved facts and freezes their detached values", () => {
  const value = original(); value.privateExtra = "never-forward-extra"; value.result.metadata.privateExtra = "never-forward-extra";
  value.attemptCompletion.cleanupCodes = ["safe.code"];
  const result = projectMetadataCommand(call(value), app, key);
  value.result.metadata.displayName = "changed later"; value.attemptCompletion.cleanupCodes.push("later");
  assert.equal(result.decision.metadata.displayName, "Café 日本語"); assert.equal(result.decision.metadata.description, "");
  assert.deepEqual(result.attemptCompletion.cleanupCodes, ["safe.code"]); assert.ok(Object.isFrozen(result.decision.metadata));
  assert.equal(JSON.stringify(result).includes("never-forward-extra"), false);
});

test("transport cutoff and protocol failures retain the exact unresolved reference", () => {
  for (const kind of ["timeout", "canceled", "transportFailure", "protocolFailure"]) {
    const result = projectMetadataCommand({ ...call(), kind, value: undefined, cleanupSucceeded: false, mutationUncertain: true }, app, key);
    assert.equal(result.disposition, "unresolved"); assert.equal(result.key, key); assert.equal(result.transport.kind, kind);
  }
  assert.throws(() => projectMetadataCommand(call(), "invalid", key), TypeError);
});
