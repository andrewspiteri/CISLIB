// Run only against the actual CIS.Management.Service host owned by the existing native fixture.
import assert from "node:assert/strict";
import { readFile, writeFile } from "node:fs/promises";
import { randomUUID } from "node:crypto";
import { ManagementClient } from "@cislib/management-client";
import { createNodeHttpsTransport } from "@cislib/management-client/server";

const args = argumentsMap(process.argv.slice(2));
const baseUrl = required("base-url");
const applicationId = required("application-id");
const operationKey = required("operation-key");
const registrationId = required("registration-id");
const reportFile = required("report-file");
const ca = await readFile(required("ca-file"));
let privateKey;
let credential;
if (args.has("bearer-file")) {
  if (args.has("certificate-file") || args.has("private-key-file")) { throw new Error("Select exactly one explicit native credential kind."); }
  const bearer = (await readFile(required("bearer-file"), "utf8")).trim();
  credential = { kind: "bearer", token: bearer };
} else {
  const certificate = await readFile(required("certificate-file"));
  privateKey = await readFile(required("private-key-file"));
  credential = { kind: "transport", value: { kind: "mtls", certificate, privateKey } };
}
try {
  const client = new ManagementClient({ baseUrl, transport: createNodeHttpsTransport({ origin: new URL(baseUrl).origin, ca }) });
  const context = { credentials: async ({ signal }) => { signal.throwIfAborted(); return credential; } };
  const workloadBearer = (await readFile(required("control-bearer-file"), "utf8")).trim();
  const workloadContext = { credentials: async ({ signal }) => {
    signal.throwIfAborted(); return { kind: "bearer", token: workloadBearer };
  } };
  const attemptId = randomUUID();
  const control = value(await client.getControl(registrationId, attemptId, workloadContext));
  assert.equal(control.registrationId, registrationId); assert.equal(control.attemptId, attemptId);
  assert.match(control.authorityId, /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/);
  assert.equal(control.admissionAllowed, true);
  assert.ok(Number.isInteger(control.validForMs) && control.validForMs >= 1 && control.validForMs <= 15000);
  const info = value(await client.getServiceInfo(context));
  assert.ok(info.protocolMajors.includes(1));
  const list = value(await client.listApplications({ pageSize: 100 }, context));
  assert.ok(list.items.some(item => item.applicationId === applicationId));
  const before = value(await client.getApplication(applicationId, context));
  const capabilities = value(await client.getCapabilities(applicationId, context));
  assert.equal(capabilities.applicationId, applicationId);
  value(await client.listInstances(applicationId, { pageSize: 100 }, context));
  value(await client.listInventory(applicationId, { pageSize: 100 }, context));
  const command = { commandVersion: 1, displayName: "Node authenticated consumer", description: "Exact Unicode: Cafe\u0301 — 日本語" };
  const applied = value(await client.replaceMetadata(applicationId, operationKey, before.metadataETag, command, context));
  assert.equal(applied.operation.key, operationKey); assert.equal(applied.originalCompletion, "committed");
  assert.ok(applied.result != null && applied.rejection == null, "The actual applied command must have its native result value.");
  assert.equal(applied.result.decision, "applied"); assert.equal(applied.result.metadata.displayName, command.displayName);
  assert.equal(applied.result.metadata.description, command.description);
  const found = value(await client.getMetadataOperation(applicationId, operationKey, context));
  assert.equal(found.resultSource, "found"); assert.equal(found.originalCompletion, "committed"); assert.deepEqual(found.result, applied.result);
  const replayed = value(await client.replaceMetadata(applicationId, operationKey, before.metadataETag, command, context));
  assert.equal(replayed.resultSource, "replayed"); assert.equal(replayed.originalCompletion, "committed"); assert.deepEqual(replayed.result, applied.result);
  const after = value(await client.getApplication(applicationId, context));
  assert.equal(after.metadataRevision, applied.result.metadataRevision); assert.equal(after.metadataETag, applied.result.metadataETag);
  const report = { protocolMajor: 1, authenticatedRead: true, metadataApplied: true, originalResultFound: true,
    replayRetainedOriginal: true, applicationId, operationKey, revision: after.metadataRevision,
    authoritativeControl: true, registrationId, authorityId: control.authorityId,
    originalCompletion: applied.originalCompletion, decidedAtUtc: applied.result.decidedAtUtc };
  await writeFile(reportFile, JSON.stringify(report, null, 2) + "\n", { flag: "wx" });
} finally { privateKey?.fill(0); }

function value(result) {
  assert.equal(result.kind, "http", "The actual authenticated HTTP exchange must complete.");
  assert.ok(result.status >= 200 && result.status < 300, "The actual service must accept the requested API.");
  assert.ok(result.value !== undefined, "The actual typed response must decode.");
  assert.equal(result.cleanupSucceeded, true, "The actual native request/socket must drain.");
  return result.value;
}
function required(name) { const entry = args.get(name); if (entry === undefined || entry.length === 0) { throw new Error("Supply --" + name + "."); } return entry; }
function argumentsMap(input) {
  const result = new Map();
  for (let index = 0; index < input.length; index += 2) {
    const name = input[index]; const item = input[index + 1];
    if (!name.startsWith("--") || item === undefined || result.has(name.slice(2))) { throw new Error("Supply unique named consumer arguments."); }
    result.set(name.slice(2), item);
  }
  return result;
}
