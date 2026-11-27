import assert from "node:assert/strict";
import test from "node:test";
import { projectConfigurationEditor, projectConfigurationCandidate } from "../src/configuration-editor-model.ts";

const app = "11111111-1111-4111-8111-111111111111", binding = "22222222-2222-4222-8222-222222222222";
const source = "33333333-3333-4333-8333-333333333333";
const raw = '{ "Large":9007199254740993,"Unicode":"Café 日本語","False":false,"Zero":0,"Null":null }';
function context() { return { bindingId: binding, applicationId: app, application: "CountryData", environment: "staging", configurationId: "settings",
  schemaVersion: "9007199254740993", hostApplicationName: "CountrySettings", selectedSource: "application",
  shape: { kind: "object", nullable: false, members: [{ name: "Large", required: true, shape: { kind: "integer", nullable: false } }] },
  commonSource: { state: "absent" }, applicationSource: { state: "available", sourceId: source }, defaults: { state: "available", defaultsJson: raw } }; }

test("unselected absence and protected content stay distinct without a guessed empty object or latest publication", () => {
  for (const content of [{ state: "unselected", message: "Choose exact references." }, { state: "absent" }, { state: "unavailable", message: "Protected." }]) {
    const projected = projectConfigurationEditor({ context: context(), content, actions: { state: "unavailable", message: "Not evaluated." } });
    assert.deepEqual(projected.content, content); assert.equal(projected.baselineRelease, undefined); assert.equal(projected.source, undefined);
    assert.equal(projected.context.defaults.defaultsJson, raw);
  }
});

test("editor and preview projection strips compatible native extensions while preserving exact lexical strings and action distinctions", () => {
  const native = context(); native.secret = "hidden"; native.shape.members[0].secret = "hidden"; native.defaults.secret = "hidden";
  const actions = { state: "available", source: { resourceId: source, secret: "hidden", actions: [{ operationId: "publishConfigurationSource", supported: false, allowed: false, reasonCode: "unsupported", secret: "hidden" }] },
    binding: { resourceId: binding, actions: [] } };
  const result = projectConfigurationEditor({ context: native, content: { state: "unselected", message: "Choose." }, actions });
  assert.equal(result.context.secret, undefined); assert.equal(result.context.shape.members[0].secret, undefined);
  assert.equal(result.context.defaults.secret, undefined); assert.equal(result.context.defaults.defaultsJson, raw);
  assert.equal(result.actions.source.secret, undefined); assert.equal(result.actions.source.actions[0].secret, undefined);
  assert.deepEqual(result.actions.source.actions[0], { operationId: "publishConfigurationSource", supported: false, allowed: false, reasonCode: "unsupported" });
  assert.ok(Object.isFrozen(result.context.shape.members));
  const candidate = projectConfigurationCandidate({ ...native, sourceId: source, sourceETag: '"source"', assignmentETag: '"map"',
    commonSource: { state: "absent" }, applicationSource: { state: "absent" }, draftSha256: "a".repeat(64), structurallyValid: true, redacted: true,
    changes: [], issues: [{ code: "protected", secret: "hidden" }], currentAssignment: { bindingId: binding, applicationId: app, configurationId: "settings",
      schemaVersion: native.schemaVersion, environment: "staging", eTag: '"map"', overrides: [] }, recipients: { items: [] }, secret: "hidden" });
  assert.equal(candidate.secret, undefined); assert.equal(candidate.issues[0].secret, undefined); assert.equal(candidate.redacted, true);
});
