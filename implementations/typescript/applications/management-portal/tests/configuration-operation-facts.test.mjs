import assert from "node:assert/strict";
import test from "node:test";
import { configurationOperationCanAdvance, mergeConfigurationOperationFact } from "../src/configuration-operation-facts.ts";

const source = "11111111-1111-4111-8111-111111111111", publication = "22222222-2222-4222-8222-222222222222";
function applied() { return { family: "management.configuration.source.publish", key: "actual-original", target: { sourceId: source },
  disposition: "applied", originalCompletion: "committed", resultSource: "prepared",
  operation: { name: "management.configuration.source.publish", key: "actual-original", scope: "actual-native-actor-scope" },
  decision: { commandVersion: 1, kind: "sourcePublished", decision: "applied", sourceId: source, publicationId: publication, sha256: "a".repeat(64),
    code: "published", decidedAtUtc: "2026-11-26T12:00:00.1234567Z", eTag: '"source.2"' },
  transport: { kind: "http", status: 200, cleanupSucceeded: false, mutationUncertain: false },
  attemptCompletion: { outcome: "committed", cleanupSucceeded: false, cleanupCodes: ["cleanup.failed"] } }; }
function weak(status = 200) { const prior = applied(); return { family: prior.family, key: prior.key, target: prior.target, disposition: "unresolved",
  originalCompletion: "unknown", resultSource: "notFound", transport: { kind: "http", status, cleanupSucceeded: true, mutationUncertain: false } }; }

test("a weaker same-original lookup preserves committed business facts but exposes its real current outcome without granting advancement", () => {
  const old = applied();
  for (const current of [weak(), weak(404), weak(403), { ...weak(), transport: { kind: "timeout", cleanupSucceeded: false, mutationUncertain: false } }]) {
    const value = mergeConfigurationOperationFact(old, current);
    assert.equal(value.decision, old.decision); assert.equal(value.operation, old.operation); assert.equal(value.originalCompletion, "committed");
    assert.equal(value.disposition, "applied"); assert.equal(value.transport, current.transport);
    assert.equal(value.attemptCompletion, undefined, "Current attempt facts are not taken from an older request.");
    assert.equal(value.recovery.state, "retained"); assert.equal(value.recovery.previousTransport.cleanupSucceeded, false);
    assert.equal(value.recovery.previousAttemptCompletion.cleanupSucceeded, false); assert.equal(value.recovery.currentOriginalCompletion, "unknown");
    assert.equal(configurationOperationCanAdvance(value), false, "A weak clean HTTP200 is not a new confirmation of the old commit.");
  }
});

test("only actual matching Found or Replay facts can confirm a later clean stage while prior cleanup facts remain dated history", () => {
  for (const resultSource of ["found", "replayed"]) {
    const old = applied(), fresh = { ...applied(), resultSource, transport: { kind: "http", status: 200, cleanupSucceeded: true, mutationUncertain: false },
      attemptCompletion: { outcome: "committed", cleanupSucceeded: true } };
    const value = mergeConfigurationOperationFact(mergeConfigurationOperationFact(old, weak()), fresh);
    assert.equal(value.recovery.state, "confirmed"); assert.equal(value.transport, fresh.transport); assert.equal(value.attemptCompletion, fresh.attemptCompletion);
    assert.equal(value.recovery.previousTransport.cleanupSucceeded, false); assert.equal(value.recovery.previousAttemptCompletion.cleanupSucceeded, false);
    assert.equal(configurationOperationCanAdvance(value), true);
  }
  const unconfirmed = mergeConfigurationOperationFact(applied(), { ...applied(), resultSource: "prepared", transport: { kind: "http", status: 200, cleanupSucceeded: true, mutationUncertain: false } });
  assert.equal(unconfirmed.recovery.state, "retained"); assert.equal(configurationOperationCanAdvance(unconfirmed), false);
});

test("a matching business result with another cleanup warning stays committed but cannot release the next Save stage", () => {
  const value = mergeConfigurationOperationFact(applied(), { ...applied(), resultSource: "found" });
  assert.equal(value.disposition, "applied"); assert.equal(value.recovery.state, "confirmed"); assert.equal(configurationOperationCanAdvance(value), false);
});

test("conflicting known decision or actor scope is inert invalid response and cannot replace the established fact", () => {
  for (const change of [{ decision: { ...applied().decision, sha256: "b".repeat(64) } }, { disposition: "rejected" },
    { originalCompletion: "notCommitted", disposition: "notCommitted" }, { operation: { ...applied().operation, scope: "foreign-actor" } }]) {
    const old = applied(), current = { ...applied(), resultSource: "found", transport: { kind: "http", status: 200, cleanupSucceeded: true, mutationUncertain: false }, ...change };
    const value = mergeConfigurationOperationFact(old, current);
    assert.equal(value.decision, old.decision); assert.equal(value.operation, old.operation); assert.equal(value.disposition, "applied");
    assert.equal(value.recovery.state, "conflicting"); assert.equal(configurationOperationCanAdvance(value), false);
  }
});

test("foreign key family or exact target is rejected rather than merged into a permitted original", () => {
  for (const current of [{ ...weak(), key: "different-original" }, { ...weak(), family: "management.configuration.release.create" },
    { ...weak(), target: { sourceId: publication } }, { ...weak(), target: { sourceId: source, extra: "not-the-original-target" } }]) {
    assert.throws(() => mergeConfigurationOperationFact(applied(), current), TypeError);
  }
});

test("unknown prior observations may be established by a real matching native result without inventing old business facts", () => {
  const current = { ...applied(), resultSource: "found" };
  assert.equal(mergeConfigurationOperationFact(weak(), current), current);
  assert.equal(mergeConfigurationOperationFact(undefined, current), current);
});
