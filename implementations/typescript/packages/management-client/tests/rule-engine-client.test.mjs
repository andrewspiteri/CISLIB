import assert from "node:assert/strict";
import test from "node:test";
import { ClientTransportFailure } from "@cislib/client-core";
import { ManagementRuleEngineClient } from "@cislib/management-client";

const applicationId = "11111111-1111-4111-8111-111111111111";
const otherApplicationId = "22222222-2222-4222-8222-222222222222";
const environment = "Development";
const rulesetId = "eligibility";
const schemaRevision = "schema-1";
const version = "33333333-3333-4333-8333-333333333333";
const editETag = '"rule-edit-1"';
const nextETag = '"rule-edit-2"';
const key = "original-save";
const savedAtUtc = "2026-11-27T12:00:00.1234567Z";
const operationName = "management.rule-engine.version.save";
const root = "/prefix/management/v1/applications/" + applicationId + "/rule-engine";

const target = { applicationId, environment, rulesetId };
const schemaJson = '{"rulesetId":"eligibility","schemaRevision":"schema-1","version":"unsaved","mode":"firstMatch","columns":[{"id":"country","displayName":"Residence country","inputKind":"string","allowedPresetKinds":["string"]}],"rows":[]}';
const definitionJson = schemaJson.replace('"rows":[]',
  '"rows":[{"ruleId":"stable-rule","ordinal":10,"state":"enabled","weight":"9007199254740993.000000000001","conditions":[{"columnId":"country","presetKind":"string","operator":"equals","preset":{"kind":"string","value":"MLT"}}],"metadata":{"original":9007199254740993.000}}]');
const savedDefinitionJson = definitionJson.replace('"version":"unsaved"', '"version":"' + version + '"');
const command = { schemaRevision, definitionJson, reason: "Save reviewed version" };

function decision(resultSource) {
  return {
    ...target,
    operation: {
      name: operationName,
      scope: "management.rule-engine.v1/" + applicationId + "/" + "a".repeat(64),
      key
    },
    resultSource,
    originalCompletion: "committed",
    result: { version, schemaRevision, editETag: nextETag, savedAtUtc }
  };
}

function response(request, value, status = 200, dispose = async () => {}) {
  const bytes = new TextEncoder().encode(JSON.stringify(value));
  return {
    status,
    url: request.url,
    redirected: false,
    headers: { "content-type": status < 300 ? "application/json" : "application/problem+json" },
    body: new ReadableStream({
      start(controller) {
        controller.enqueue(bytes);
        controller.close();
      }
    }),
    dispose
  };
}

test("typed rule engine reads and save recovery use exact routes, keys and lossless definition text", async () => {
  const calls = [];
  let credentials = 0;
  let disposals = 0;
  const replies = [
    { items: [{ ...target, displayName: "Eligibility", schemaRevision, editETag }] },
    { ...target, schemaRevision, schemaJson },
    { items: [{ ...target, version, schemaRevision, savedAtUtc }] },
    { ...target, version, schemaRevision, savedAtUtc, definitionJson: savedDefinitionJson },
    decision("prepared"),
    decision("found")
  ];
  const client = new ManagementRuleEngineClient({
    baseUrl: "https://management.example.test/prefix",
    credentials: async () => {
      credentials++;
      return { kind: "none" };
    },
    transport: {
      async send(request) {
        calls.push(request);
        return response(request, replies[calls.length - 1], 200, async () => {
          disposals++;
        });
      }
    }
  });

  assert.equal((await client.listRulesets(applicationId)).value.items[0].editETag, editETag);
  assert.equal((await client.getSchema(applicationId, rulesetId, schemaRevision)).value.schemaJson, schemaJson);
  assert.equal((await client.listVersions(applicationId, rulesetId)).value.items[0].savedAtUtc, savedAtUtc);
  assert.equal((await client.getVersion(applicationId, rulesetId, version)).value.definitionJson, savedDefinitionJson);
  assert.equal((await client.saveVersion(applicationId, rulesetId, key, editETag, command)).value.result.version, version);
  assert.equal((await client.getSaveOperation(applicationId, rulesetId, key)).value.resultSource, "found");

  assert.deepEqual(calls.map(call => call.method), ["GET", "GET", "GET", "GET", "POST", "GET"]);
  assert.deepEqual(calls.map(call => new URL(call.url).pathname), [
    root + "/rulesets", root + "/schema", root + "/versions",
    root + "/version", root + "/versions", root + "/version-operations/" + key
  ]);
  for (const call of calls.slice(1)) {
    assert.equal(new URL(call.url).searchParams.get("rulesetId"), rulesetId);
  }
  assert.equal(new URL(calls[1].url).searchParams.get("schemaRevision"), schemaRevision);
  assert.equal(new URL(calls[3].url).searchParams.get("version"), version);
  assert.equal(calls[4].headers["If-Match"], editETag);
  assert.equal(calls[4].headers["Idempotency-Key"], key);
  assert.deepEqual(JSON.parse(new TextDecoder().decode(calls[4].body)), command);
  assert.equal(calls[5].body, undefined);
  assert.equal(calls[5].headers["If-Match"], undefined);
  assert.equal(credentials, 6);
  assert.equal(disposals, 6);
});

test("schema reads check the case-sensitive embedded identities and refuse saved rows", async () => {
  for (const invalidSchema of [
    schemaJson.replace('"rulesetId":"eligibility"', '"rulesetId":"Eligibility"'),
    schemaJson.replace('"schemaRevision":"schema-1"', '"schemaRevision":"Schema-1"'),
    savedDefinitionJson
  ]) {
    const client = new ManagementRuleEngineClient({
      baseUrl: "https://management.example.test",
      transport: {
        async send(request) {
          return response(request, { ...target, schemaRevision, schemaJson: invalidSchema });
        }
      }
    });
    const outcome = await client.getSchema(applicationId, rulesetId, schemaRevision);
    assert.equal(outcome.kind, "protocolFailure");
    assert.equal(outcome.value, undefined);
  }
});

test("save rejects a weak precondition or open command and does not retry uncertain native transport", async () => {
  let credentials = 0;
  let sends = 0;
  const client = new ManagementRuleEngineClient({
    baseUrl: "https://management.example.test",
    readRetries: 2,
    credentials: async () => {
      credentials++;
      return { kind: "none" };
    },
    transport: {
      async send() {
        sends++;
        throw new ClientTransportFailure("network.lost");
      }
    }
  });

  await assert.rejects(async () => client.saveVersion(applicationId, rulesetId, key, 'W/"weak"', command));
  await assert.rejects(async () => client.saveVersion(applicationId, rulesetId, key, editETag, { ...command, deploy: true }));
  assert.equal(credentials, 0);
  assert.equal(sends, 0);

  const uncertain = await client.saveVersion(applicationId, rulesetId, key, editETag, command);

  assert.equal(uncertain.kind, "transportFailure");
  assert.equal(uncertain.mutationUncertain, true);
  assert.equal(credentials, 1);
  assert.equal(sends, 1);
});

test("save does not expose foreign application, rule identity or original key even in a typed problem", async () => {
  const mutations = [
    value => { value.applicationId = otherApplicationId; },
    value => { value.rulesetId = "another-set"; },
    value => { value.operation.key = "another-key"; }
  ];
  for (const mutate of mutations) {
    for (const status of [200, 503]) {
      let disposals = 0;
      const value = decision("prepared");
      mutate(value);
      const client = new ManagementRuleEngineClient({
        baseUrl: "https://management.example.test",
        transport: {
          async send(request) {
            const payload = status === 200
              ? value
              : { type: "about:blank", title: "Try lookup", status, code: "cleanup.failed", operation: value.operation, result: value };
            return response(request, payload, status, async () => {
              disposals++;
            });
          }
        }
      });
      const outcome = await client.saveVersion(applicationId, rulesetId, key, editETag, command);
      assert.equal(outcome.kind, "protocolFailure");
      assert.equal(outcome.value, undefined);
      assert.equal(outcome.problem, undefined);
      assert.equal(disposals, 1);
    }
  }
});

test("deployment reads and original receipts keep desired state scoped to the assignment map", async () => {
  const rulesetId = "case +/%2F ";
  const scope = { applicationId, environment, rulesetId };
  const prior = '"assignment-0"';
  const assignmentETag = '"assignment-1"';
  const desired = { version, schemaRevision, releaseId: otherApplicationId, generation: "1" };
  const operation = { ...decision("prepared").operation, name: "management.rule-engine.deployment.request" };
  const intent = "rollback";
  const result = { intent, assignmentETag, requestedAtUtc: savedAtUtc, desired };
  const saved = { ...scope, operation, resultSource: "prepared", originalCompletion: "committed", result };
  const replies = [
    { ...scope, assignmentETag: prior },
    saved,
    { ...scope, assignmentETag, desired },
    { ...saved, resultSource: "found" },
    { ...scope, assignmentETag, desired: { ...desired, version: "draft" } }
  ];
  const calls = [];
  const client = new ManagementRuleEngineClient({
    baseUrl: "https://management.example.test/prefix",
    transport: {
      async send(request) {
        calls.push(request);
        return response(request, replies[calls.length - 1]);
      }
    }
  });
  const deployment = { version, intent, reason: "Restore a known selection" };

  assert.deepEqual((await client.getDeployment(applicationId, rulesetId)).value, replies[0]);
  assert.deepEqual((await client.requestDeployment(applicationId, rulesetId, key, prior, deployment)).value, saved);
  assert.deepEqual((await client.getDeployment(applicationId, rulesetId)).value, replies[2]);
  assert.deepEqual((await client.getDeploymentOperation(applicationId, rulesetId, key)).value, replies[3]);
  const invalidSelection = await client.getDeployment(applicationId, rulesetId);
  assert.equal(invalidSelection.kind, "protocolFailure");
  assert.equal(invalidSelection.value, undefined);
  assert.deepEqual(calls.map(call => call.method), ["GET", "POST", "GET", "GET", "GET"]);
  assert.deepEqual(calls.map(call => new URL(call.url).pathname), [
    root + "/deployment", root + "/deployments", root + "/deployment", root + "/deployment-operations/" + key, root + "/deployment"
  ]);
  assert.ok(calls.every(call => new URL(call.url).searchParams.get("rulesetId") === rulesetId));
  assert.equal(calls[1].headers["Idempotency-Key"], key);
  assert.equal(calls[1].headers["If-Match"], prior);
  assert.deepEqual(JSON.parse(new TextDecoder().decode(calls[1].body)), deployment);
  assert.equal(calls[3].headers["Idempotency-Key"], undefined);
  assert.equal(calls[3].headers["If-Match"], undefined);
});

test("deployment uncertainty retains its original key and rejects facts from the save operation family", async () => {
  const operation = { ...decision("prepared").operation, name: "management.rule-engine.deployment.request" };
  const deploymentResult = {
    ...target, operation, resultSource: "unknown", originalCompletion: "unknown",
    attemptCompletion: { outcome: "notCommitted", cleanupSucceeded: true }
  };
  const problem = {
    type: "about:blank", title: "Check the original result", status: 503, code: "operation.unknown", operation,
    deploymentResult
  };
  const replies = [
    problem,
    { ...problem, operation: decision("prepared").operation, result: decision("prepared"), deploymentResult: undefined }
  ];
  let calls = 0;
  const client = new ManagementRuleEngineClient({
    baseUrl: "https://management.example.test", readRetries: 2,
    transport: {
      async send(request) {
        return response(request, replies[calls++], 503);
      }
    }
  });
  const deployment = { version, intent: "deploy", reason: "Select reviewed version" };
  await assert.rejects(client.requestDeployment(applicationId, rulesetId, key, editETag, { ...deployment, rollout: true }));
  await assert.rejects(client.requestDeployment(applicationId, rulesetId, key, editETag, { ...deployment, version: "draft" }));
  assert.equal(calls, 0);

  const unknown = await client.requestDeployment(applicationId, rulesetId, key, editETag, deployment);
  assert.equal(unknown.kind, "http");
  assert.equal(unknown.operationKey, key);
  assert.equal(unknown.mutationUncertain, true);
  assert.deepEqual(unknown.problem.deploymentResult, deploymentResult);
  assert.equal(unknown.value, undefined);

  const wrongFamily = await client.requestDeployment(applicationId, rulesetId, key, editETag, deployment);
  assert.equal(wrongFamily.kind, "protocolFailure");
  assert.equal(wrongFamily.problem, undefined);
  assert.equal(calls, 2);
});

test("version notes and current deployment timestamps preserve native optional facts", async () => {
  const note = '  Review <change> & exact "wording"  ';
  const desired = { version, schemaRevision, releaseId: otherApplicationId, generation: "2" };
  const summary = { ...target, version, schemaRevision, savedAtUtc, versionNote: note };
  const replies = [
    { items: [summary] },
    { ...summary, definitionJson: savedDefinitionJson },
    { ...target, assignmentETag: editETag, desired, requestedAtUtc: savedAtUtc },
    { ...target, assignmentETag: editETag, requestedAtUtc: savedAtUtc }
  ];
  let sent = 0;
  const client = new ManagementRuleEngineClient({
    baseUrl: "https://management.example.test",
    transport: { async send(request) { return response(request, replies[sent++]); } }
  });

  assert.equal((await client.listVersions(applicationId, rulesetId)).value.items[0].versionNote, note);
  assert.equal((await client.getVersion(applicationId, rulesetId, version)).value.versionNote, note);
  assert.equal((await client.getDeployment(applicationId, rulesetId)).value.requestedAtUtc, savedAtUtc);
  const unassignedWithTimestamp = await client.getDeployment(applicationId, rulesetId);
  assert.equal(unassignedWithTimestamp.kind, "protocolFailure");
  assert.equal(unassignedWithTimestamp.value, undefined);
});

test("deployment observations use the signed page cursor and validate native nested adoption", async () => {
  const configuration = {
    applicationId, environment, configurationId: "rule-config", schemaVersion: "1", registrationId: version,
    registrationFreshness: "fresh", adoptionFreshness: "stale", convergence: "pending",
    desired: { releaseId: otherApplicationId, generation: "2" }
  };
  const observation = { rulesetId, configuration, requestedVersion: version };
  const cursor = "signed.scope";
  const replies = [
    { items: [observation], nextCursor: cursor },
    { items: [{ ...observation, configuration: { ...configuration, desired: { ...configuration.desired, generation: 2 } } }] },
    { items: [{ ...observation, configuration: { ...configuration, applicationId: otherApplicationId } }] },
    { items: [{ ...observation, appliedVersion: version }] }
  ];
  const calls = [];
  const client = new ManagementRuleEngineClient({
    baseUrl: "https://management.example.test/prefix",
    transport: { async send(request) {
      calls.push(request);
      return response(request, replies[calls.length - 1]);
    } }
  });

  const first = await client.listDeploymentInstances(applicationId, rulesetId, { pageSize: 2 });
  assert.deepEqual(first.value, replies[0]);
  const malformed = await client.listDeploymentInstances(applicationId, rulesetId, { pageSize: 2, cursor });
  assert.equal(malformed.kind, "protocolFailure");
  assert.equal(malformed.value, undefined);
  const foreign = await client.listDeploymentInstances(applicationId, rulesetId, { pageSize: 2, cursor });
  assert.equal(foreign.kind, "protocolFailure");
  assert.equal(foreign.value, undefined);
  const unsupportedApplied = await client.listDeploymentInstances(applicationId, rulesetId, { pageSize: 2, cursor });
  assert.equal(unsupportedApplied.kind, "protocolFailure");
  assert.equal(unsupportedApplied.value, undefined);
  const continued = new URL(calls[1].url);
  assert.equal(continued.pathname, root + "/deployment-instances");
  assert.deepEqual(Object.fromEntries(continued.searchParams), { rulesetId, pageSize: "2", cursor });
});

const executionId = "44444444-4444-4444-8444-444444444444";
const instanceId = "55555555-5555-4555-8555-555555555555";
const registrationId = "66666666-6666-4666-8666-666666666666";
const authorityId = "77777777-7777-4777-8777-777777777777";
const startAuditId = "88888888-8888-4888-8888-888888888888";
const completionAuditId = "99999999-9999-4999-8999-999999999999";
const finishedAtUtc = "2026-11-27T12:00:00.1234568Z";
const execution = { ...target, executionId, version, schemaRevision, producerApplication: "RuleConsumer", instanceId, registrationId,
  applicationBuild: "1.0.0+evidence", startedAtUtc: savedAtUtc, status: "Completed", startReceived: true,
  completionReceived: true, finishedAtUtc };

function executionAudit(completion) {
  const phase = completion ? "complete" : "start";
  const occurredAtUtc = completion ? finishedAtUtc : savedAtUtc;
  const details = { executionId, authorityId, applicationId, application: execution.producerApplication, environment, instanceId,
    registrationId, applicationBuild: execution.applicationBuild, engineContractVersion: 1, rulesetId, schemaRevision, version,
    startedAtUtc: savedAtUtc.replace("Z", "+00:00"),
    ...(completion ? { finishedAtUtc: finishedAtUtc.replace("Z", "+00:00"), status: "Completed",
      rows: [{ rowIndex: 0, state: "Enabled", outcome: "Matched", cellOutcomes: "m" }], selectedRowIndices: [0] }
      : { inputs: [{ columnId: "unknown", kind: "json", value: "EXACT_NUMBER" }] }) };
  return JSON.stringify({
    auditVersion: 1, auditId: completion ? completionAuditId : startAuditId, eventType: "cis.rule-engine.execution." + phase, eventVersion: 1,
    category: "business", occurredAtUtc, recordedAtUtc: occurredAtUtc,
    producer: { application: execution.producerApplication, environment, instance: { kind: "reported", reportedIdentifier: instanceId } },
    actor: { kind: "unknown" }, outcome: completion ? "succeeded" : "accepted",
    ...(completion ? { causationAuditId: startAuditId } : {}), domainEventId: executionId + ":" + phase, details
  }).replace('"EXACT_NUMBER"', "9007199254740993.000");
}

test("execution history binds exact filters and preserves paired original evidence", async () => {
  const cursor = "signed.history";
  const filters = { executionId, version, instanceId, status: "Completed", startedFromUtc: savedAtUtc, startedThroughUtc: finishedAtUtc };
  const detail = { execution, definitionJson: savedDefinitionJson, startAuditJson: executionAudit(false), completionAuditJson: executionAudit(true) };
  const replies = [{ items: [execution], nextCursor: cursor }, detail];
  const calls = [];
  const client = new ManagementRuleEngineClient({
    baseUrl: "https://management.example.test/prefix",
    transport: { async send(request) {
      calls.push(request);
      return response(request, replies[calls.length - 1]);
    } }
  });
  const entries = await client.listExecutions(applicationId, rulesetId, filters, { pageSize: 2, cursor });
  assert.deepEqual(entries.value, replies[0]);
  const original = await client.getExecution(applicationId, rulesetId, executionId);
  assert.deepEqual(original.value, detail);
  assert.ok(original.value.startAuditJson.includes("9007199254740993.000"));
  const listed = new URL(calls[0].url);
  const selected = new URL(calls[1].url);
  assert.equal(listed.pathname, root + "/executions");
  assert.deepEqual(Object.fromEntries(listed.searchParams), { rulesetId, ...filters, pageSize: "2", cursor });
  assert.equal(selected.pathname, root + "/executions/" + executionId);
  assert.deepEqual(Object.fromEntries(selected.searchParams), { rulesetId });
});

test("execution history distinguishes incomplete phases and rejects conflicting protected evidence", async () => {
  const startAuditJson = executionAudit(false);
  const completionAuditJson = executionAudit(true);
  const detail = { execution, definitionJson: savedDefinitionJson, startAuditJson, completionAuditJson };
  const completionOnly = { ...detail, execution: { ...execution, status: "Incomplete", startReceived: false }, startAuditJson: undefined };
  const replies = [completionOnly, { ...detail, completionAuditJson: completionAuditJson.replace(startAuditId, authorityId) },
    { ...detail, definitionJson }, { items: [{ ...execution, status: "Incomplete" }] }];
  let received = 0;
  const client = new ManagementRuleEngineClient({
    baseUrl: "https://management.example.test",
    transport: { async send(request) { return response(request, replies[received++]); } }
  });
  const incomplete = await client.getExecution(applicationId, rulesetId, executionId);
  assert.equal(incomplete.value.execution.status, "Incomplete");
  assert.equal(incomplete.value.execution.finishedAtUtc, finishedAtUtc);
  assert.equal(incomplete.value.startAuditJson, undefined);
  for (let index = 0; index < 2; index++) {
    const rejected = await client.getExecution(applicationId, rulesetId, executionId);
    assert.equal(rejected.kind, "protocolFailure");
    assert.equal(rejected.value, undefined);
  }
  const rejectedList = await client.listExecutions(applicationId, rulesetId, { status: "Completed" });
  assert.equal(rejectedList.kind, "protocolFailure");
});

const counterOperation = {
  name: "management.rule-engine.counter.reset",
  scope: "management.rule-engine.v1/" + applicationId + "/" + "b".repeat(64),
  key
};

test("counter reads retain exact counts, freshness and independently paged reset outcomes", async () => {
  const ruleId = "retained +/%";
  const missing = "missing";
  const count = "9007199254740993";
  const cursor = "signed.counter";
  const outcomeCursor = "signed.outcome";
  const command = { selection: "selected", ruleIds: [ruleId, missing], reason: "Reset reviewed counters" };
  const prepared = { ...target, operation: counterOperation, resultSource: "prepared", originalCompletion: "committed",
    result: { selection: "selected", resetAtUtc: savedAtUtc, appliedCount: "1", rejectedCount: "1" } };
  const replies = [
    { ...target, availability: "fresh", observedAtUtc: savedAtUtc, items: [{ ruleId, count }], nextCursor: cursor },
    { ...target, availability: "stale", observedAtUtc: savedAtUtc, items: [{ ruleId, count, lastResetAtUtc: savedAtUtc }] },
    prepared,
    { ...prepared, resultSource: "found" },
    { ...target, operation: counterOperation, items: [{ ruleId, disposition: "reset" }], nextCursor: outcomeCursor },
    { ...target, operation: counterOperation, items: [{ ruleId: missing, disposition: "notFound" }] },
    { ...target, availability: "unavailable", items: [{ ruleId }] }
  ];
  const calls = [];
  const client = new ManagementRuleEngineClient({
    baseUrl: "https://management.example.test/prefix",
    transport: { async send(request) {
      calls.push(request);
      return response(request, replies[calls.length - 1]);
    } }
  });

  const fresh = await client.listCounters(applicationId, rulesetId, { pageSize: 1 });
  assert.equal(fresh.value.items[0].count, count);
  const stale = await client.listCounters(applicationId, rulesetId, { pageSize: 1, cursor });
  assert.equal(stale.value.availability, "stale");
  assert.equal(stale.value.items[0].lastResetAtUtc, savedAtUtc);
  const reset = await client.resetCounters(applicationId, rulesetId, key, command);
  assert.deepEqual(reset.value.result, prepared.result);
  const original = await client.getCounterResetOperation(applicationId, rulesetId, key);
  assert.equal(original.value.resultSource, "found");
  const first = await client.listCounterResetOutcomes(applicationId, rulesetId, key, { pageSize: 1 });
  assert.equal(first.value.items[0].disposition, "reset");
  const next = await client.listCounterResetOutcomes(applicationId, rulesetId, key, { pageSize: 1, cursor: first.value.nextCursor });
  assert.equal(next.value.items[0].disposition, "notFound");
  const unavailable = await client.listCounters(applicationId, rulesetId);
  assert.equal(unavailable.value.availability, "unavailable");
  assert.equal(unavailable.value.items[0].count, undefined);

  assert.equal(calls[2].method, "POST");
  assert.equal(calls[2].headers["Idempotency-Key"], key);
  assert.equal(calls[2].headers["If-Match"], undefined);
  assert.deepEqual(JSON.parse(new TextDecoder().decode(calls[2].body)), command);
  assert.equal(calls[3].headers["Idempotency-Key"], undefined);
  assert.deepEqual(calls.map(call => new URL(call.url).pathname), [
    root + "/counters", root + "/counters", root + "/counter-resets", root + "/counter-reset-operations/" + key,
    root + "/counter-reset-operations/" + key + "/outcomes", root + "/counter-reset-operations/" + key + "/outcomes", root + "/counters"
  ]);
  assert.deepEqual(Object.fromEntries(new URL(calls[5].url).searchParams), { rulesetId, pageSize: "1", cursor: outcomeCursor });
});

test("counter resets retain unresolved original keys and reject unbound or invented facts", async () => {
  const command = { selection: "all", ruleIds: [], reason: "Reset all retained counters" };
  const counterResetResult = { ...target, operation: counterOperation, resultSource: "unknown", originalCompletion: "unknown" };
  const problem = { type: "about:blank", title: "Check the original result", status: 409, code: "operation.contended",
    operation: counterOperation, counterResetResult };
  const replies = [
    [problem, 409],
    [{ ...target, operation: { ...counterOperation, key: "foreign-reset" }, items: [{ ruleId: "a", disposition: "reset" }] }, 200],
    [{ ...target, availability: "unavailable", items: [{ ruleId: "a", count: "0" }] }, 200]
  ];
  let sent = 0;
  const client = new ManagementRuleEngineClient({
    baseUrl: "https://management.example.test", readRetries: 2,
    transport: { async send(request) {
      const [value, status] = replies[sent++];
      return response(request, value, status);
    } }
  });

  await assert.rejects(client.resetCounters(applicationId, rulesetId, key, { ...command, ruleIds: ["a"] }));
  await assert.rejects(client.resetCounters(applicationId, rulesetId, key, { ...command, selection: "selected", ruleIds: ["a", "a"] }));
  assert.equal(sent, 0);
  const unknown = await client.resetCounters(applicationId, rulesetId, key, command);
  assert.equal(unknown.mutationUncertain, true);
  assert.equal(unknown.operationKey, key);
  assert.deepEqual(unknown.problem.counterResetResult, counterResetResult);
  assert.equal(sent, 1);
  assert.equal((await client.listCounterResetOutcomes(applicationId, rulesetId, key)).kind, "protocolFailure");
  assert.equal((await client.listCounters(applicationId, rulesetId)).kind, "protocolFailure");
});

test("execution history exposes returned matches only for consistent paired completions", async () => {
  const detail = { execution: { ...execution, returnedMatchCount: 1 }, definitionJson: savedDefinitionJson,
    startAuditJson: executionAudit(false), completionAuditJson: executionAudit(true) };
  const empty = { ...detail, execution: { ...execution, returnedMatchCount: 0 },
    completionAuditJson: detail.completionAuditJson.replace('"outcome":"Matched","cellOutcomes":"m"', '"outcome":"NotMatched","cellOutcomes":"n"')
      .replace('"selectedRowIndices":[0]', '"selectedRowIndices":[]') };
  const replies = [
    { items: [detail.execution] }, detail, empty, { ...detail, execution: { ...execution, returnedMatchCount: 0 } },
    { items: [{ ...execution, status: "Incomplete", startReceived: false, returnedMatchCount: 0 }] },
    { items: [{ ...execution, returnedMatchCount: 49 }] },
    { items: [{ ...execution, returnedMatchCount: 0.5 }] }
  ];
  let sent = 0;
  const client = new ManagementRuleEngineClient({
    baseUrl: "https://management.example.test",
    transport: { async send(request) { return response(request, replies[sent++]); } }
  });

  assert.equal((await client.listExecutions(applicationId, rulesetId)).value.items[0].returnedMatchCount, 1);
  assert.equal((await client.getExecution(applicationId, rulesetId, executionId)).value.execution.returnedMatchCount, 1);
  assert.equal((await client.getExecution(applicationId, rulesetId, executionId)).value.execution.returnedMatchCount, 0);
  const wrongCount = await client.getExecution(applicationId, rulesetId, executionId);
  assert.equal(wrongCount.kind, "protocolFailure");
  assert.equal(wrongCount.value, undefined);
  for (let index = 0; index < 3; index++) {
    const rejected = await client.listExecutions(applicationId, rulesetId);
    assert.equal(rejected.kind, "protocolFailure");
    assert.equal(rejected.value, undefined);
  }
});
