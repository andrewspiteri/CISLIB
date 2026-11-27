import assert from "node:assert/strict";
import test from "node:test";
import { metadataCommand, metadataConfirmationAction, metadataDraftText, metadataTargetMatches, projectMetadataEditor, readMetadataDraft, resolvedMetadataOriginal, reviewMetadata } from "../src/metadata-draft.ts";

const applicationId = "11111111-1111-1111-1111-111111111111";
const snapshot = { applicationId, application: "fixed.identity", environment: "Development", metadataRevision: "9007199254740993",
  metadataETag: '"metadata-5"', metadata: { displayName: "Before", description: "Private description" }, canReplace: true };

test("description clearing is reviewed explicitly and submits omission, preserving exact Unicode", () => {
  const draft = { displayName: "Café 🌍 e\u0301", description: "" };
  const review = reviewMetadata(snapshot, JSON.stringify(draft));
  assert.deepEqual(review.command, { commandVersion: 1, displayName: draft.displayName });
  assert.equal("description" in review.command, false);
  assert.equal(review.before.description, "Private description");
  assert.equal(review.ifMatch, snapshot.metadataETag);
  assert.equal(review.applicationId, applicationId);
  assert.ok(Object.isFrozen(review)); assert.ok(Object.isFrozen(review.command));
});

test("native scalar limits reject empty names, excessive values, invalid Unicode and null clearing", () => {
  for (const draft of [
    { displayName: "", description: "" }, { displayName: "x".repeat(257), description: "" },
    { displayName: "Valid", description: "x".repeat(2049) }, { displayName: "\ud800", description: "" },
    { displayName: "Valid", description: null }, { displayName: "Valid", description: "", application: "forged" }
  ]) { assert.throws(() => metadataCommand(draft)); }
  assert.equal(metadataCommand({ displayName: "🌍".repeat(256), description: "🌍".repeat(2048) }).description.length, 4096);
});

test("review binds the original ETag without adding identity fields or normalizing text", () => {
  const text = metadataDraftText({ displayName: "  exact name  ", description: "exact e\u0301 text" });
  assert.deepEqual(readMetadataDraft(text), { displayName: "  exact name  ", description: "exact e\u0301 text" });
  const review = reviewMetadata(snapshot, text);
  assert.deepEqual(Object.keys(review.command), ["commandVersion", "displayName", "description"]);
  assert.throws(() => reviewMetadata({ ...snapshot, canReplace: false }, text));
  assert.throws(() => reviewMetadata({ ...snapshot, metadataETag: 'W/"weak"' }, text));
});

test("draft hydration is closed and cannot introduce an actor, target or persisted operation", () => {
  for (const value of [null, { displayName: "After", description: null },
    { displayName: "After", description: "", actor: "forged" }, { displayName: "After", description: "", applicationId },
    { displayName: "After", description: "", key: "invented-operation" }]) {
    assert.throws(() => readMetadataDraft(JSON.stringify(value)));
  }
});

test("retained metadata and review cannot be rebound when the mounted editor target changes", () => {
  const requestKey = `/api/portal/applications/${applicationId}/metadata`;
  const target = { requestKey, applicationId, environment: snapshot.environment };
  const other = "22222222-2222-2222-2222-222222222222";
  assert.equal(metadataTargetMatches(applicationId, requestKey, snapshot, target), true);
  assert.equal(metadataTargetMatches(other, requestKey.replace(applicationId, other), snapshot, target), false);
  assert.equal(metadataTargetMatches(applicationId, requestKey, snapshot, { ...target, environment: "Production" }), false);
  assert.equal(metadataTargetMatches(applicationId, requestKey, snapshot, { ...target, requestKey: requestKey + "/other" }), false);
  assert.throws(() => reviewMetadata(snapshot, metadataDraftText(snapshot.metadata), other));
});

test("known originals lock further lookup until explicit dismissal, while unknown results remain recoverable", () => {
  for (const disposition of ["applied", "rejected", "conflict"]) {
    assert.equal(resolvedMetadataOriginal({ disposition, originalCompletion: "committed" }), true);
    assert.equal(resolvedMetadataOriginal({ disposition, originalCompletion: "unknown" }), false);
  }
  assert.equal(resolvedMetadataOriginal({ disposition: "notCommitted", originalCompletion: "notCommitted" }), true);
  assert.equal(resolvedMetadataOriginal({ disposition: "unresolved", originalCompletion: "unknown" }), false);
  assert.equal(resolvedMetadataOriginal({ disposition: "unresolved", originalCompletion: "future" }), false);
  assert.equal(resolvedMetadataOriginal(undefined), false);
});

test("fresh exact metadata confirmation returns only its committed draft and preserves independent cleanup faults", () => {
  const text = metadataDraftText({ displayName: "After", description: "Visible application description" });
  const review = reviewMetadata(snapshot, text);
  const key = "22222222-2222-4222-8222-222222222222";
  const value = { family: "management.metadata.replace", target: { applicationId }, key, disposition: "applied", originalCompletion: "committed", resultSource: "prepared",
    decision: { applicationId, decision: "applied", metadata: { displayName: "After", description: "Visible application description" } },
    transport: { kind: "http", status: 200, cleanupSucceeded: true, mutationUncertain: false } };
  assert.equal(metadataConfirmationAction(review, value, key, text), "return");
  assert.equal(metadataConfirmationAction(review, { ...value, resultSource: "replayed" }, key, text), "return");
  assert.equal(metadataConfirmationAction(review, { ...value, transport: { ...value.transport, cleanupSucceeded: false } }, key, text), "show-commit");
  assert.equal(metadataConfirmationAction(review, { ...value, transport: { ...value.transport, kind: "transportFailure", mutationUncertain: true } }, key, text), "show-commit");
  assert.equal(metadataConfirmationAction(review, { ...value, attemptCompletion: { cleanupSucceeded: false } }, key, text), "show-commit");
  for (const altered of [{ ...value, key: "another-original" }, { ...value, target: { applicationId: key } },
    { ...value, originalCompletion: "unknown" }, { ...value, resultSource: "found" }, { ...value, resultSource: "future" },
    { ...value, disposition: "conflict" }, { ...value, decision: { ...value.decision, applicationId: key } },
    { ...value, decision: { ...value.decision, metadata: { ...value.decision.metadata, description: "Unreviewed content" } } }]) {
    assert.equal(metadataConfirmationAction(review, altered, key, text), "stay");
  }
  assert.equal(metadataConfirmationAction(review, value, key, metadataDraftText({ displayName: "Newer unsaved draft", description: "" })), "stay");
  assert.equal(metadataConfirmationAction(review, value, key, undefined), "stay");
  assert.equal(metadataConfirmationAction(review, undefined, key, text), "stay");
});

test("private editor projection excludes additive native metadata and unrelated application fields", () => {
  const projected = projectMetadataEditor({ ...snapshot, metadata: { ...snapshot.metadata, futurePrivateField: "must-not-cross" },
    token: "must-not-cross", nativeExtensions: { private: "must-not-cross" } }, true);
  assert.deepEqual(projected, snapshot);
  assert.equal(JSON.stringify(projected).includes("must-not-cross"), false);
  assert.ok(Object.isFrozen(projected.metadata));
});
