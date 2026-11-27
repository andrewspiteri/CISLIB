import test from "node:test";
import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import {
  decodeScheduler, encodeScheduler, SchedulerWireError, captureSchedulerWireLimits, knownCapabilityKind,
  knownCapabilityOperation, knownFormEditor, knownSchedulerRunState, knownSchedulerAttemptState,
  knownSchedulerContentAccess, knownSchedulerCompletion
} from "@cislib/management-client";
import { parseWireJson } from "../packages/management-client/dist/wire-json.js";

const catalog = JSON.parse(await readFile(new URL("../../../conformance/management/v1/scheduler-cases.json", import.meta.url), "utf8"));
assert.equal(catalog.caseCount, 98);
assert.equal(catalog.cases.length, catalog.caseCount);
for (const vector of catalog.cases) {
  test("portable scheduler semantics: " + vector.id, async () => {
    // `valid` includes native semantic rules; schemaValid alone deliberately does not.
    if (!vector.valid) {
      await assert.rejects(decodeScheduler(vector.schema, vector.json, vector.direction), SchedulerWireError);
      return;
    }
    const value = await decodeScheduler(vector.schema, vector.json, vector.direction);
    assert.ok(Object.isFrozen(value));
    const roundTrip = await decodeScheduler(vector.schema, await encodeScheduler(vector.schema, value, vector.direction), vector.direction);
    assert.deepEqual(roundTrip, value);
  });
}

const fixture = id => JSON.parse(catalog.cases.find(row => row.id === id).json);
const capsule = () => fixture("scheduler-capsule-existing-command-and-original-business-key");
const attempt = () => fixture("scheduler-recovery-number-is-not-business-retry-ceiling");
const run = () => fixture("scheduler-run-attempt-counter-is-lossless");

test("wire capture is deeply immutable and does not retain the mutable encode input", async () => {
  const value = fixture("scheduler-capability-logical-job-definition");
  const originalVersion = value.key.definitionVersion;
  const encoded = await encodeScheduler("CapabilityDefinition", value, "response");
  value.key.definitionVersion = "9";
  const decoded = await decodeScheduler("CapabilityDefinition", encoded, "response");
  assert.equal(decoded.key.definitionVersion, originalVersion);
  assert.ok(Object.isFrozen(decoded.key)); assert.ok(Object.isFrozen(decoded.form.fields));
  assert.throws(() => { decoded.form.fields[0].label = "Changed"; }, TypeError);
});

test("unknown response additions stay inert while declared integer tokens remain exact", async () => {
  const text = '{"name":"customer/input","version":1,"future":{"order":1.25,"status":2e1}}';
  const decoded = await decodeScheduler("MessageContract", text, "response");
  assert.equal(decoded.future.order, 1.25);
  for (const version of ["1.0", "1e0"]) {
    await assert.rejects(decodeScheduler("MessageContract", text.replace('"version":1', '"version":' + version), "response"), SchedulerWireError);
  }
  for (const [schema, input] of [
    ["CommandSchedulerRetryPolicy", '{"enabled":true,"maximumAttempts":2e0,"initialDelayMs":"5000","maximumDelayMs":"6000"}'],
    ["CommandSchedulerCalendar", '{"form":"weekly","missedRunPolicy":"skip","localTime":"12:00:00.0000000","timeZoneId":"UTC","weekdays":[1.0]}']
  ]) { await assert.rejects(decodeScheduler(schema, input, "request"), SchedulerWireError); }
});

test("opaque captured payload hashes exact UTF-8 text and rejects a pre-canceled decode", async () => {
  const value = capsule().jobSettings;
  value.payloadJson = '{"Label":"e\u0301"}';
  await assert.rejects(decodeScheduler("SchedulerCapturedSettings", JSON.stringify(value), "response"), SchedulerWireError);
  const controller = new AbortController(); controller.abort();
  await assert.rejects(decodeScheduler("SchedulerCapturedSettings", JSON.stringify(capsule().jobSettings), "response", {}, controller.signal), { name: "AbortError" });
});

test("captured content requires explicit allowed access, including future access values", async () => {
  const detail = { run: run(), attempts: [attempt()], capturedContentAccess: "allowed", capsule: capsule() };
  await decodeScheduler("SchedulerRunDetail", JSON.stringify(detail), "response");
  for (const access of ["redacted", "expired", "unavailable", "futureAccess"]) {
    detail.capturedContentAccess = access;
    await assert.rejects(decodeScheduler("SchedulerRunDetail", JSON.stringify(detail), "response"), SchedulerWireError);
    const safe = { ...detail }; delete safe.capsule;
    assert.equal((await decodeScheduler("SchedulerRunDetail", JSON.stringify(safe), "response")).capturedContentAccess, access);
  }
  const missing = { ...detail, capturedContentAccess: "allowed" }; delete missing.capsule;
  await assert.rejects(decodeScheduler("SchedulerRunDetail", JSON.stringify(missing), "response"), SchedulerWireError);
  const event = { applicationId: detail.run.trigger.applicationId, triggerId: detail.run.trigger.triggerId,
    eventId: detail.run.eventId, occurredAtUtc: detail.run.createdAtUtc, receivedAtUtc: detail.run.createdAtUtc,
    contract: { name: "calendar.event", version: 1 }, producer: detail.capsule.producer, payloadAccess: "allowed", payloadJson: "null" };
  await decodeScheduler("SchedulerEvent", JSON.stringify(event), "response");
  event.payloadAccess = "unknown";
  await assert.rejects(decodeScheduler("SchedulerEvent", JSON.stringify(event), "response"), SchedulerWireError);
});

test("capsule identity and one attempt page cannot describe a different run or event source", async () => {
  const detail = { run: run(), attempts: [attempt()], capturedContentAccess: "allowed", capsule: capsule() };
  for (const mutate of [value => { value.attempts[0].runId = value.run.eventId; },
    value => { value.capsule.trigger.triggerId = value.run.runId; }, value => { value.capsule.job.definitionVersion = "2"; }]) {
    const changed = structuredClone(detail); mutate(changed);
    await assert.rejects(decodeScheduler("SchedulerRunDetail", JSON.stringify(changed), "response"), SchedulerWireError);
  }
});

test("existing Messaging envelope preserves broader business keys and its raw payload bound", async () => {
  const value = capsule(); const envelope = JSON.parse(value.inputEnvelopeJson);
  assert.match(envelope.operation.key, /[?:/]/u);
  for (const rawPayload of ['{"value":' + " ".repeat(65530) + "0}", '"' + "\\u0061".repeat(11000) + '"']) {
    value.inputEnvelopeJson = JSON.stringify(envelope).replace(/"payload":\{"PartitionId":"Europe"\}/u, '"payload":' + rawPayload);
    assert.ok(new TextEncoder().encode(value.inputEnvelopeJson).length < 81920);
    await assert.rejects(decodeScheduler("SchedulerRunCapsule", JSON.stringify(value), "response"), SchedulerWireError);
  }
  value.inputEnvelopeJson = JSON.stringify({ ...envelope, payload: null, createdAtUtc: "2026-11-25T01:00:00Z" });
  await decodeScheduler("SchedulerRunCapsule", JSON.stringify(value), "response");
  const parsed = parseWireJson('{"payload": { "a":1 }, "details":{"large":9007199254740993}}', 16);
  assert.equal(parsed.rawValues.size, 0); assert.equal(parsed.objects.get("/details"), '{"large":9007199254740993}');
  assert.equal(parseWireJson('{"payload": [ 1,2 ]}', 16, new Set(["/payload"])).rawValues.get("/payload"), "[ 1,2 ]");
});

test("existing Messaging metadata ceiling counts original whitespace outside the raw payload", async () => {
  const value = capsule(); const envelope = value.inputEnvelopeJson;
  const payload = parseWireJson(envelope, 16, new Set(["/payload"])).rawValues.get("/payload");
  const metadataBytes = new TextEncoder().encode(envelope).length - new TextEncoder().encode(payload).length;
  value.inputEnvelopeJson = envelope.replace('"envelopeVersion":', '"envelopeVersion":' + " ".repeat(16384 - metadataBytes));
  await decodeScheduler("SchedulerRunCapsule", JSON.stringify(value), "response");
  value.inputEnvelopeJson += " ";
  assert.ok(new TextEncoder().encode(value.inputEnvelopeJson).length < 81920);
  await assert.rejects(decodeScheduler("SchedulerRunCapsule", JSON.stringify(value), "response"), SchedulerWireError);
});

test("future bounded response job kinds remain inert through run and captured detail decoding", async () => {
  const detail = { run: run(), attempts: [attempt()], capturedContentAccess: "allowed", capsule: capsule() };
  detail.run.job.kind = "futureJob"; detail.capsule.job.kind = "futureJob";
  const decoded = await decodeScheduler("SchedulerRunDetail", JSON.stringify(detail), "response");
  assert.equal(decoded.run.job.kind, "futureJob");
  assert.deepEqual(knownCapabilityKind(decoded.capsule.job.kind), { value: "futureJob", isKnown: false });
  const command = fixture("scheduler-task-command-passive-exact-settings"); command.definition.job.kind = "futureJob";
  await assert.rejects(decodeScheduler("SchedulerTaskCommand", JSON.stringify(command), "request"), SchedulerWireError);
});

test("a complete calendar preview and complete task preview are distinct from partial context", async () => {
  const common = { valid: true, issues: [], affectedTasks: [], impactFullyDisclosed: true,
    nextOccurrencesUtc: ["2026-11-25T01:00:00.0000000Z"], activationState: "pending" };
  await decodeScheduler("SchedulerTaskPreview", JSON.stringify(common), "response");
  const definition = fixture("scheduler-task-command-passive-exact-settings").definition;
  const complete = { ...common, draftDigest: "a".repeat(64), triggerETag: '"trigger-1"', preconditions: [definition.jobConfiguration], definition };
  await decodeScheduler("SchedulerTaskPreview", JSON.stringify(complete), "response");
  for (const field of ["draftDigest", "triggerETag", "preconditions", "definition"]) {
    const partial = { ...complete }; delete partial[field];
    await assert.rejects(decodeScheduler("SchedulerTaskPreview", JSON.stringify(partial), "response"), SchedulerWireError);
  }
  for (const preconditions of [[], [{ ...definition.jobConfiguration, assignmentGeneration: "2" }], [definition.jobConfiguration, definition.jobConfiguration]]) {
    await assert.rejects(decodeScheduler("SchedulerTaskPreview", JSON.stringify({ ...complete, preconditions }), "response"), SchedulerWireError);
  }
});

test("raw complete definition and capsule bounds include escaped and whitespace token text", async () => {
  const definition = fixture("scheduler-capability-logical-job-definition");
  const raw = JSON.stringify(definition).replace('"displayName":"Customer dormancy"', '"displayName":"' + "\\u0041".repeat(200) + '"');
  await assert.rejects(decodeScheduler("CapabilityDefinition", raw, "response", { maximumDefinitionBytes: 1000 }), SchedulerWireError);
  await assert.rejects(decodeScheduler("CapabilityDefinitionPage", '{"items":[' + raw + "]}", "response", { maximumDefinitionBytes: 1000 }), SchedulerWireError);
  const value = capsule(); const text = JSON.stringify(value); const rawCapsule = text.replace('"runId":', '"runId":' + " ".repeat(1000));
  await assert.rejects(decodeScheduler("SchedulerRunCapsule", rawCapsule, "response", { maximumCapsuleBytes: new TextEncoder().encode(text).length + 500 }), SchedulerWireError);
});

test("fixed delay keeps one cycle while initial and wall-clock controls cannot conflict", async () => {
  for (const missedRunPolicy of ["skip", "runLatestOnce", "catchUpBounded"]) {
    await decodeScheduler("CommandSchedulerCalendar", JSON.stringify({ form: "fixedDelay", missedRunPolicy, delayMs: "1000" }), "request");
  }
  const fixed = { form: "fixedDelay", missedRunPolicy: "skip", delayMs: "1000", initialDelayMs: "0", runImmediately: true };
  await decodeScheduler("CommandSchedulerCalendar", JSON.stringify(fixed), "request");
  for (const change of [{ runImmediately: false }, { initialDelayMs: "1" }]) {
    await assert.rejects(decodeScheduler("CommandSchedulerCalendar", JSON.stringify({ ...fixed, ...change }), "request"), SchedulerWireError);
  }
  const daily = { form: "daily", missedRunPolicy: "skip", localTime: "09:30:00.0000000", timeZoneId: "Europe/Malta" };
  for (const change of [{ runImmediately: true }, { failurePolicy: "continue" }, { timeZoneId: "../Malta" }, { timeZoneId: "Local" }]) {
    await assert.rejects(decodeScheduler("CommandSchedulerCalendar", JSON.stringify({ ...daily, ...change }), "request"), SchedulerWireError);
  }
});

test("admission needs the exact retained attempt and known recovery cannot change original outcome", async () => {
  for (const disposition of ["admitted", "replayed"]) {
    await assert.rejects(decodeScheduler("SchedulerAttemptAdmission", JSON.stringify({ disposition }), "response"), SchedulerWireError);
  }
  const value = fixture("scheduler-original-commit-survives-current-cleanup");
  for (const resultSource of ["found", "replayed"]) {
    await assert.rejects(decodeScheduler("SchedulerOperationResult", JSON.stringify({ ...value, resultSource, originalCompletion: "unknown" }), "response"), SchedulerWireError);
  }
});

test("finite outer and nested ceilings remain independently configurable only downward", async () => {
  assert.throws(() => captureSchedulerWireLimits({ maximumBodyBytes: 1048577 }), RangeError);
  assert.throws(() => captureSchedulerWireLimits({ maximumMappings: 0 }), RangeError);
  const value = capsule();
  await assert.rejects(decodeScheduler("SchedulerRunCapsule", JSON.stringify(value), "response", { maximumCapsuleBytes: 128 }), SchedulerWireError);
  await assert.rejects(decodeScheduler("SchedulerRunCapsule", JSON.stringify(value), "response", { maximumPayloadBytes: 1 }), SchedulerWireError);
  for (const input of [new Uint8Array([255]), new Uint8Array([239, 187, 191, 123, 125]), "\ufeff{}", '{"name":"x","version":1,"\\u0076ersion":1}']) {
    await assert.rejects(decodeScheduler("MessageContract", input, "response"), SchedulerWireError);
  }
});

test("future state and catalog tokens are explicitly unknown and cleanup never rewrites original commit", async () => {
  for (const helper of [knownCapabilityKind, knownCapabilityOperation, knownFormEditor, knownSchedulerRunState,
    knownSchedulerAttemptState, knownSchedulerContentAccess, knownSchedulerCompletion]) { assert.deepEqual(helper("future"), { value: "future", isKnown: false }); }
  const original = await decodeScheduler("SchedulerOperationResult", JSON.stringify(fixture("scheduler-original-commit-survives-current-cleanup")), "response");
  assert.equal(original.originalCompletion, "committed"); assert.equal(original.attemptCompletion.cleanupSucceeded, false);
});
