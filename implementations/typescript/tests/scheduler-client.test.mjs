import test from "node:test";
import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import { ManagementSchedulerClient, ManagementApplicationCatalogClient, ManagementConfigurationAdministrationClient } from "@cislib/management-client";
import { ClientTransportFailure } from "@cislib/client-core";

const cases = JSON.parse(await readFile(new URL("../../../conformance/management/v1/scheduler-cases.json", import.meta.url), "utf8"));
const sample = id => JSON.parse(cases.cases.find(row => row.id === id).json);
const capsule = sample("scheduler-capsule-existing-command-and-original-business-key");
const run = sample("scheduler-run-attempt-counter-is-lossless");
const attempt = sample("scheduler-attempt-observes-acquisition-and-monotonic-duration");
const taskCommand = sample("scheduler-task-command-passive-exact-settings");
const eventCommand = sample("scheduler-event-command-retains-correlation-scalars");
const attemptCommand = sample("scheduler-attempt-command-retains-native-effective-policy");
const report = sample("scheduler-report-duration-is-lossless-monotonic-value");
const app = run.applicationId, triggerId = run.trigger.triggerId, taskId = run.taskId, runId = run.runId, eventId = run.eventId;
const registrationId = attempt.registrationId, key = "original-scheduler-key", etag = '"current-resource"';
const time = run.createdAtUtc;
const base = `applications/${app}/scheduler`;
const triggerCommand = { commandVersion: 1, triggerId, reason: "Review source", definition: { name: "Daily source",
  provider: { ...run.job, kind: "triggerProvider", definitionId: "calendar.daily" }, configuration: taskCommand.definition.jobConfiguration, enabled: false } };
const trigger = { applicationId: app, triggerId, revision: "1", eTag: etag, definition: triggerCommand.definition, state: "disabled",
  configuredBy: capsule.configuredBy, changedAtUtc: time };
const task = { applicationId: app, taskId, desiredRevision: "1", eTag: etag, definition: taskCommand.definition, state: "disabled",
  configuredBy: capsule.configuredBy, changedAtUtc: time };
const previewCommand = { commandVersion: 1, definition: taskCommand.definition, taskId };
const taskPreview = { valid: true, issues: [], affectedTasks: [], impactFullyDisclosed: true, nextOccurrencesUtc: [], activationState: "pending",
  definition: taskCommand.definition, draftDigest: "a".repeat(64), taskETag: etag, triggerETag: etag, preconditions: [taskCommand.definition.jobConfiguration] };
const calendarCommand = { commandVersion: 1, calendar: { form: "daily", missedRunPolicy: "runLatestOnce", localTime: "09:30:00.0000000", timeZoneId: "UTC" }, fromUtc: time, count: 2 };
const calendarPreview = { valid: true, issues: [], affectedTasks: [], impactFullyDisclosed: true, nextOccurrencesUtc: [time], activationState: "preview" };
const event = { applicationId: app, triggerId, eventId, occurredAtUtc: time, receivedAtUtc: time, contract: eventCommand.contract,
  producer: capsule.producer, payloadAccess: "redacted" };
const receipt = { applicationId: app, triggerId, eventId, disposition: "accepted", acceptedAtUtc: time };
const reportReceipt = { registrationId, stream: "scheduler-attempt", submittedSequence: report.sequence, acceptedSequence: report.sequence,
  disposition: "accepted", receivedAtUtc: time };
const state = { commandVersion: 1, intent: "disable", reason: "Operator review" };
const manual = { commandVersion: 1, eventId, reason: "Operator requested occurrence" };
const cancellation = { commandVersion: 1, intent: "cancel", reason: "Operator requested cancellation" };

test("all scheduler routes retain exact source identities prefix and guarded creation update state and cancellation headers", async () => {
  const rows = [
    [c => c.listTriggers(app), "GET", base + "/triggers?pageSize=100", { items: [trigger] }],
    [c => c.getTrigger(app, triggerId), "GET", base + "/triggers/" + triggerId, trigger],
    [c => c.createTrigger(app, key, triggerCommand), "POST", base + "/triggers", original("trigger", "triggerCreated", triggerId), "create"],
    [c => c.updateTrigger(app, triggerId, key, etag, triggerCommand), "PUT", base + "/triggers/" + triggerId, original("trigger", "triggerUpdated", triggerId), "match"],
    [c => c.changeTriggerState(app, triggerId, key, etag, state), "POST", base + "/triggers/" + triggerId + "/state", original("trigger", "triggerStateChanged", triggerId), "match"],
    [c => c.getTriggerOperation(app, triggerId, key), "GET", base + "/triggers/" + triggerId + "/operations/" + key, original("trigger", "triggerUpdated", triggerId)],
    [c => c.listTasks(app), "GET", base + "/tasks?pageSize=100", { items: [task] }],
    [c => c.getTask(app, taskId), "GET", base + "/tasks/" + taskId, task],
    [c => c.createTask(app, key, taskCommand), "POST", base + "/tasks", original("task", "taskCreated", taskId), "create"],
    [c => c.updateTask(app, taskId, key, etag, taskCommand), "PUT", base + "/tasks/" + taskId, original("task", "taskUpdated", taskId), "match"],
    [c => c.changeTaskState(app, taskId, key, etag, state), "POST", base + "/tasks/" + taskId + "/state", original("task", "taskStateChanged", taskId), "match"],
    [c => c.getTaskOperation(app, taskId, key), "GET", base + "/tasks/" + taskId + "/operations/" + key, original("task", "taskActivated", taskId)],
    [c => c.previewTask(app, previewCommand), "POST", base + "/tasks/preview", taskPreview],
    [c => c.previewCalendar(app, calendarCommand), "POST", base + "/calendar/preview", calendarPreview],
    [c => c.runTaskNow(app, taskId, key, etag, manual), "POST", base + "/tasks/" + taskId + "/runs", original("task", "manualRunAdmitted", taskId), "match"],
    [c => c.cancelRun(app, runId, key, cancellation), "POST", base + "/runs/" + runId + "/cancellation", original("run", "cancellationRequested", runId), "key"],
    [c => c.getRunOperation(app, runId, key), "GET", base + "/runs/" + runId + "/operations/" + key, original("run", "cancellationRequested", runId)],
    [c => c.getRuns(app, { taskId }), "GET", base + "/runs?pageSize=100&taskId=" + taskId, { items: [run] }],
    [c => c.getRun(app, runId), "GET", base + "/runs/" + runId, { run, attempts: [attempt], capturedContentAccess: "redacted" }],
    [c => c.getAttempts(app, runId), "GET", base + "/runs/" + runId + "/attempts?pageSize=100", { items: [attempt] }],
    [c => c.submitEvent(app, triggerId, eventCommand), "POST", base + "/triggers/" + triggerId + "/events", receipt],
    [c => c.getEventReceipt(app, triggerId, eventId), "GET", base + "/triggers/" + triggerId + "/events/" + eventId + "/receipt", receipt],
    [c => c.getEvent(app, triggerId, eventId), "GET", base + "/triggers/" + triggerId + "/events/" + eventId, event],
    [c => c.getEventRuns(app, triggerId, eventId), "GET", base + "/triggers/" + triggerId + "/events/" + eventId + "/runs?pageSize=100", { items: [run] }],
    [c => c.getReadyRuns(registrationId), "GET", "registrations/" + registrationId + "/scheduler/ready-runs?pageSize=100", { items: [run] }],
    [c => c.getRunCapsule(registrationId, runId), "GET", "registrations/" + registrationId + "/scheduler/runs/" + runId + "/capsule", capsule],
    [c => c.admitAttempt(app, runId, attemptCommand), "POST", base + "/runs/" + runId + "/attempts", { disposition: "admitted", attempt }],
    [c => c.reportAttempt(app, runId, attempt.attemptId, report), "PUT", base + "/runs/" + runId + "/attempts/" + attempt.attemptId + "/report", reportReceipt]
  ];
  for (const [invoke, method, suffix, body, header] of rows) {
    const requests = []; let disposed = 0; const observations = [];
    const client = new ManagementSchedulerClient({ baseUrl: "https://example.test/prefix", observe: value => observations.push(value),
      transport: { async send(request) { requests.push(request); return response(request, 200, body, {}, async () => { disposed++; }); } } });
    const result = await invoke(client);
    assert.equal(result.kind, "http", suffix); assert.deepEqual(result.value, body, suffix); assert.equal(disposed, 1, suffix);
    assert.equal(requests.length, 1); assert.equal(requests[0].method, method); assert.equal(requests[0].url, "https://example.test/prefix/management/v1/" + suffix);
    assert.equal(observations[0].operation, "scheduler");
    if (header !== undefined) { assert.equal(requests[0].headers["Idempotency-Key"], key); }
    assert.equal(requests[0].headers["If-None-Match"], header === "create" ? "*" : undefined);
    assert.equal(requests[0].headers["If-Match"], header === "match" ? etag : undefined);
  }
});

test("writes events admission reports and readonly POST never automatically retry or invent keys", async () => {
  const calls = [
    [c => c.createTask(app, key, taskCommand), true, key], [c => c.updateTask(app, taskId, key, etag, taskCommand), true, key],
    [c => c.runTaskNow(app, taskId, key, etag, manual), true, key], [c => c.cancelRun(app, runId, key, cancellation), true, key],
    [c => c.submitEvent(app, triggerId, eventCommand), true], [c => c.admitAttempt(app, runId, attemptCommand), true],
    [c => c.reportAttempt(app, runId, attempt.attemptId, report), true], [c => c.previewTask(app, previewCommand), false],
    [c => c.previewCalendar(app, calendarCommand), false]
  ];
  for (const [invoke, mutates, expectedKey] of calls) {
    for (const http of [false, true]) {
      let sends = 0;
      const client = new ManagementSchedulerClient({ baseUrl: "https://example.test/", readRetries: 2, transport: { async send(request) {
        sends++; if (!http) { throw new ClientTransportFailure("transport.unavailable"); }
        return response(request, 503, problem(503), { "content-type": "application/problem+json", "retry-after": "0" });
      } } });
      const result = await invoke(client);
      assert.equal(result.mutationUncertain, mutates); assert.equal(result.operationKey, expectedKey); assert.equal(sends, 1);
    }
  }
  let reads = 0;
  const client = new ManagementSchedulerClient({ baseUrl: "https://example.test/", readRetries: 1, transport: { async send(request) {
    reads++; return reads === 1 ? response(request, 503, problem(503), { "content-type": "application/problem+json", "retry-after": "0" })
      : response(request, 200, { items: [] });
  } } });
  assert.equal((await client.listTasks(app)).kind, "http"); assert.equal(reads, 2);
});

test("committed original decision or rejection survives HTTP failure and expected cleanup failure", async () => {
  for (const rejected of [false, true]) {
    const retained = original("task", "taskCreated", taskId);
    if (rejected) { delete retained.result; retained.rejection = { code: "native.refused", message: "Original refusal" }; }
    const client = new ManagementSchedulerClient({ baseUrl: "https://example.test/", transport: { async send(request) {
      return response(request, 500, { ...problem(500), operation: retained.operation, result: retained },
        { "content-type": "application/problem+json" }, async () => { throw new ClientTransportFailure("cleanup.failed"); });
    } } });
    const result = await client.createTask(app, key, taskCommand);
    assert.equal(result.kind, "http"); assert.equal(result.status, 500); assert.equal(result.cleanupSucceeded, false);
    assert.equal(result.problem.result.originalCompletion, "committed"); assert.equal(result.mutationUncertain, false);
    assert.equal(result.problem.result.rejection?.code, rejected ? "native.refused" : undefined);
  }
});

test("guard and response echoes reject wrong target family source receipt or preview context", async () => {
  let sends = 0;
  const invalid = new ManagementSchedulerClient({ baseUrl: "https://example.test/", transport: { async send() { sends++; throw new Error("must not send"); } } });
  for (const invoke of [c => c.createTask(app, ".", taskCommand), c => c.updateTask(app, taskId, key, 'W/"weak"', taskCommand),
    c => c.getEvent(app, "wrong", eventId), c => c.getRuns(app, { eventId })]) { await assert.rejects(invoke(invalid)); }
  assert.equal(sends, 0);
  const bads = [
    [c => c.createTask(app, key, taskCommand), { ...original("task", "taskCreated", taskId), operation: { ...original("task", "taskCreated", taskId).operation, key: "foreign" } }],
    [c => c.createTask(app, key, taskCommand), original("trigger", "triggerCreated", triggerId)],
    [c => c.getEventRuns(app, triggerId, eventId), { items: [{ ...run, trigger: { ...run.trigger, triggerId: taskId } }] }],
    [c => c.getRuns(app, { taskId }), { items: [{ ...run, taskId: runId }] }],
    [c => c.getRuns(app, { executionScope: run.job.executionScope, jobId: run.job.definitionId }), { items: [{ ...run, job: { ...run.job, definitionId: "different.job" } }] }],
    [c => c.getRuns(app, { eventId, triggerApplicationId: app, triggerId }), { items: [{ ...run, trigger: { ...run.trigger, applicationId: taskId } }] }],
    [c => c.getRuns(app, { taskId, resourceId: run.resourceId }), { items: [{ ...run, resourceId: "Different resource" }] }],
    [c => c.getRuns(app, { taskId, state: "succeeded" }), { items: [run] }],
    [c => c.reportAttempt(app, runId, attempt.attemptId, report), { ...reportReceipt, stream: "inventory" }],
    [c => c.admitAttempt(app, runId, attemptCommand), { disposition: "admitted", attempt: { ...attempt, acquisitionId: taskId } }],
    [c => c.admitAttempt(app, runId, attemptCommand), { disposition: "admitted", attempt: { ...attempt, effectivePolicy: "different-policy" } }],
    [c => c.previewTask(app, previewCommand), calendarPreview],
    [c => c.previewTask(app, previewCommand), { ...taskPreview, definition: { ...taskPreview.definition, resource: { kind: "constant", constant: "Different resource" } } }],
    [c => c.previewTask(app, previewCommand), { ...taskPreview, preconditions: [] }],
    [c => c.previewTask(app, previewCommand), { ...taskPreview, preconditions: [taskPreview.preconditions[0], taskPreview.preconditions[0]] }],
    [c => c.previewCalendar(app, calendarCommand), taskPreview],
    [c => c.previewCalendar(app, calendarCommand), { ...calendarPreview, nextOccurrencesUtc: [time, time, time] }]
  ];
  for (const [invoke, body] of bads) {
    const client = new ManagementSchedulerClient({ baseUrl: "https://example.test/", transport: { async send(request) { return response(request, 200, body); } } });
    assert.equal((await invoke(client)).kind, "protocolFailure");
  }
});

test("detached sequence and per-call credentials stay exact during mutable caller and simultaneous calls", async () => {
  const mutable = { ...report }; const tokens = [];
  const client = new ManagementSchedulerClient({ baseUrl: "https://example.test/", transport: { async send(request) {
    tokens.push(request.authentication.token);
    if (request.method === "PUT") { mutable.sequence = "999"; return response(request, 200, reportReceipt); }
    return response(request, 200, { items: [] });
  } } });
  const credentials = token => async () => ({ kind: "bearer", token });
  assert.equal((await client.reportAttempt(app, runId, attempt.attemptId, mutable, { credentials: credentials("report") })).kind, "http");
  await Promise.all([client.listTasks(app, undefined, { credentials: credentials("user-a") }),
    client.listTasks(app, undefined, { credentials: credentials("user-b") })]);
  assert.deepEqual(tokens.slice().sort(), ["report", "user-a", "user-b"]);
});

test("scheduler digest decode remains inside deadline and joins actual cleanup", async () => {
  const entered = deferred(), release = deferred(); let disposed = false, settled = false, call;
  const native = globalThis.crypto;
  const restore = replaceCrypto(async (...args) => { entered.resolve(); await release.promise; return native.subtle.digest(...args); });
  try {
    const client = new ManagementSchedulerClient({ baseUrl: "https://example.test/", timeoutMs: 50, transport: { async send(request) {
      return response(request, 200, capsule, {}, async () => { disposed = true; });
    } } });
    call = client.getRunCapsule(registrationId, runId); void call.then(() => { settled = true; }, () => { settled = true; });
    await Promise.race([entered.promise, call.then(() => { throw new Error("Capsule decoding did not perform the owned integrity check."); })]);
    await pause(80); assert.equal(settled, false); assert.equal(disposed, false);
    release.resolve(); const result = await call; assert.equal(result.kind, "timeout"); assert.equal(disposed, true); assert.equal(result.value, undefined);
  } finally { release.resolve(); if (call !== undefined) { await call.catch(() => undefined); } restore(); }
});

test("catalog and configuration administration companions preserve query tuple and existing problem families", async () => {
  const definition = sample("scheduler-capability-logical-job-definition");
  const exactKey = { ...definition.key, executionScope: "scope/é", definitionId: "job/α" };
  const configured = { ...definition, key: exactKey };
  const requests = [], observations = [];
  const catalog = new ManagementApplicationCatalogClient({ baseUrl: "https://example.test/prefix/", observe: value => observations.push(value),
    transport: { async send(request) { requests.push(request); return response(request, 200, request.url.includes("capability-definition?") ? configured : { items: [] }); } } });
  assert.equal((await catalog.getCapabilityDefinition(app, exactKey)).kind, "http");
  const url = new URL(requests[0].url); assert.equal(url.pathname, "/prefix/management/v1/applications/" + app + "/capability-definition");
  assert.equal(url.searchParams.get("executionScope"), "scope/é"); assert.equal(url.searchParams.get("definitionId"), "job/α");
  await catalog.getCapabilityDefinitions(app, { executionScope: "scope/é", definitionVersion: "9007199254740993" }, { pageSize: 2, cursor: "opaque+/" });
  assert.equal(new URL(requests[1].url).searchParams.get("cursor"), "opaque+/"); assert.equal(observations[0].operation, "catalog");
  await catalog.getCapabilityParticipation(app);
  const paths = [];
  const admin = new ManagementConfigurationAdministrationClient({ baseUrl: "https://example.test/", transport: { async send(request) {
    paths.push(new URL(request.url).pathname + new URL(request.url).search);
    return response(request, 403, { ...problem(403), code: "configuration.denied" }, { "content-type": "application/problem+json" });
  } } });
  assert.equal((await admin.getSourceActions(app)).problem.code, "configuration.denied");
  await admin.getBindingActions(app, "settings", "1"); await admin.getCohorts(app, "settings", "1", { pageSize: 2 });
  assert.deepEqual(paths, ["/management/v1/configuration/sources/" + app + "/actions",
    "/management/v1/applications/" + app + "/configuration/settings/actions?schemaVersion=1",
    "/management/v1/applications/" + app + "/configuration/settings/cohorts?schemaVersion=1&pageSize=2"]);
  const actions = { resourceId: app, actions: [{ operationId: "publishConfigurationSource", supported: true, allowed: false }] };
  const successful = new ManagementConfigurationAdministrationClient({ baseUrl: "https://example.test/", transport: { async send(request) {
    return response(request, 200, request.url.includes("/cohorts?") ? { items: [{ cohortId: "canary", displayName: "Canary" }] } : actions);
  } } });
  assert.deepEqual((await successful.getSourceActions(app)).value, actions);
  assert.deepEqual((await successful.getBindingActions(app, "settings", "1")).value, actions);
  assert.equal((await successful.getCohorts(app, "settings", "1")).value.items[0].displayName, "Canary");
});

test("unexpected digest and cleanup faults retain both original identities", async () => {
  const original = new Error("digest sentinel"), cleanup = new Error("cleanup sentinel");
  const restore = replaceCrypto(async () => { throw original; });
  try {
    const client = new ManagementSchedulerClient({ baseUrl: "https://example.test/", transport: { async send(request) {
      return response(request, 200, capsule, {}, async () => { throw cleanup; });
    } } });
    await assert.rejects(client.getRunCapsule(registrationId, runId), error => error instanceof AggregateError
      && error.errors[0] === original && error.errors[1] === cleanup);
  } finally { restore(); }
});

function original(family, kind, objectId) {
  const value = sample("scheduler-original-commit-survives-current-cleanup");
  value.operation.name = "management.scheduler." + family + ".write"; value.operation.key = key;
  value.result.kind = kind; value.result.applicationId = app; value.result.objectId = objectId; return value;
}
function problem(status) { return { type: "about:blank", title: "Unavailable", status, code: "scheduler.unavailable" }; }
function response(request, status, body, headers = {}, dispose = async () => {}) {
  return { status, url: request.url, redirected: false, headers: { "content-type": "application/json", ...headers },
    body: body === undefined ? null : new ReadableStream({ start(controller) { controller.enqueue(new TextEncoder().encode(JSON.stringify(body))); controller.close(); } }), dispose };
}
function deferred() { let resolve; const promise = new Promise(done => { resolve = done; }); return { promise, resolve }; }
function pause(ms) { return new Promise(resolve => setTimeout(resolve, ms)); }
function replaceCrypto(digest) {
  const descriptor = Object.getOwnPropertyDescriptor(globalThis, "crypto");
  Object.defineProperty(globalThis, "crypto", { configurable: true, value: { subtle: { digest } } });
  return () => { if (descriptor === undefined) { delete globalThis.crypto; } else { Object.defineProperty(globalThis, "crypto", descriptor); } };
}
