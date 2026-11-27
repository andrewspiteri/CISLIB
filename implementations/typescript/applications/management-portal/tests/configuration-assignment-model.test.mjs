import assert from "node:assert/strict";
import test from "node:test";
import { beginConfigurationAssignment, buildConfigurationAssignmentReview, chooseConfigurationAssignmentDefault,
  chooseConfigurationAssignmentOverride, ConfigurationAssignmentReviewError, configurationAssignmentCommand,
  configurationAssignmentRows, nextConfigurationAssignmentGeneration, removeConfigurationAssignmentOverride,
  setConfigurationAssignmentIntent, setConfigurationAssignmentReason } from "../src/configuration-assignment-model.ts";

const app = "11111111-1111-4111-8111-111111111111";
const binding = "22222222-2222-4222-8222-222222222222";
const first = "33333333-3333-4333-8333-333333333333";
const candidate = "44444444-4444-4444-8444-444444444444";
const finance = "55555555-5555-4555-8555-555555555555";
function map() {
  return { bindingId: binding, applicationId: app, configurationId: "settings", schemaVersion: "9007199254740993",
    environment: "staging", eTag: '"configuration.map.9007199254740993"', generation: "9007199254740993", defaultReleaseId: first,
    overrides: [{ cohortId: "canary", releaseId: candidate }, { cohortId: "finance", releaseId: finance }] };
}
function release(releaseId = candidate) {
  return { releaseId, applicationId: app, configurationId: "settings", schemaVersion: "9007199254740993", environment: "staging",
    commonSource: { state: "absent" }, applicationSource: { state: "absent" }, actor: "operator", reason: "Reviewed exact content",
    createdAtUtc: "2026-11-26T00:00:00.0000000Z", eTag: '"retained"' };
}
function reason(draft) { return setConfigurationAssignmentReason(draft, "  Exact reason é 日本語  "); }
function preview(draft, { redacted = false } = {}) {
  return { applicationId: app, configurationId: "settings", schemaVersion: "9007199254740993", structurallyValid: true, redacted,
    changes: [{ path: "/overrides/canary", kind: "retained" }], issues: [], currentAssignment: map(),
    proposedAssignment: configurationAssignmentCommand(draft), recipients: { items: [], nextCursor: "opaque-page" } };
}
function reviewError(code) {
  return error => error instanceof ConfigurationAssignmentReviewError && error.code === code;
}

test("whole-map draft detaches every current pin and retains opaque unrelated release facts", () => {
  const current = map();
  const draft = beginConfigurationAssignment(current);
  current.overrides[1].releaseId = first; current.overrides.push({ cohortId: "new", releaseId: candidate }); current.defaultReleaseId = candidate;
  assert.equal(draft.defaultReleaseId, first);
  assert.deepEqual(draft.overrides, [{ cohortId: "canary", releaseId: candidate }, { cohortId: "finance", releaseId: finance }]);
  assert.equal(draft.baseline.generation, "9007199254740993");
  assert.ok(Object.isFrozen(draft)); assert.ok(Object.isFrozen(draft.baseline)); assert.ok(Object.isFrozen(draft.overrides[1]));
  assert.throws(() => draft.overrides.splice(0, 1), TypeError);
  assert.equal(configurationAssignmentRows(draft).find(row => row.cohortId === "finance").change, "retained");
});

test("canary changes only its exact trusted choice and promotion removal is explicit", () => {
  const base = beginConfigurationAssignment(map());
  let canary = chooseConfigurationAssignmentOverride(base, { cohortId: "canary", displayName: "Canary" }, release(first));
  assert.equal(canary.defaultReleaseId, first);
  assert.equal(canary.overrides.find(item => item.cohortId === "finance").releaseId, finance);
  assert.equal(canary.overrides.find(item => item.cohortId === "canary").releaseId, first);
  let promotion = reason(chooseConfigurationAssignmentDefault(setConfigurationAssignmentIntent(base, "promote"), release()));
  assert.equal(promotion.overrides.find(item => item.cohortId === "canary").releaseId, candidate, "Promotion does not silently remove a canary pin.");
  promotion = removeConfigurationAssignmentOverride(promotion, "canary");
  const command = configurationAssignmentCommand(promotion);
  assert.equal(command.intent, "promote"); assert.equal(command.defaultReleaseId, candidate);
  assert.deepEqual(command.overrides, [{ cohortId: "finance", releaseId: finance }]);
  assert.deepEqual(configurationAssignmentRows(promotion), [
    { kind: "default", beforeReleaseId: first, afterReleaseId: candidate, change: "changed" },
    { kind: "cohort", cohortId: "canary", beforeReleaseId: candidate, change: "removed" },
    { kind: "cohort", cohortId: "finance", beforeReleaseId: finance, afterReleaseId: finance, change: "retained" }
  ]);
  assert.deepEqual(base.overrides, map().overrides);
});

test("rollback and initial assignment retain exact increasing generation facts without release-version inference", () => {
  const rollback = reason(chooseConfigurationAssignmentDefault(setConfigurationAssignmentIntent(beginConfigurationAssignment(map()), "rollback"), release(first)));
  assert.equal(configurationAssignmentCommand(rollback).intent, "rollback");
  assert.equal(nextConfigurationAssignmentGeneration(rollback.baseline), "9007199254740994");
  assert.equal(rollback.baseline.generation, "9007199254740993");
  assert.equal(nextConfigurationAssignmentGeneration({ ...map(), generation: "9223372036854775807" }), undefined);
  const empty = { ...map(), overrides: [], defaultReleaseId: undefined, generation: undefined };
  const unassigned = beginConfigurationAssignment(empty);
  assert.equal(nextConfigurationAssignmentGeneration(empty), "1");
  assert.throws(() => configurationAssignmentCommand(reason(unassigned)), TypeError);
  assert.equal(configurationAssignmentCommand(reason(chooseConfigurationAssignmentDefault(unassigned, release()))).defaultReleaseId, candidate);
});

test("finite pin controls refuse duplicate overflow invented identifiers and foreign selected release tuples", () => {
  const all = { ...map(), overrides: Array.from({ length: 16 }, (_, index) => ({ cohortId: "cohort-" + index, releaseId: first })) };
  const full = beginConfigurationAssignment(all);
  assert.throws(() => chooseConfigurationAssignmentOverride(full, { cohortId: "extra", displayName: "Extra" }, release()), TypeError);
  assert.equal(chooseConfigurationAssignmentOverride(full, { cohortId: "cohort-0", displayName: "Existing" }, release()).overrides.length, 16);
  assert.throws(() => beginConfigurationAssignment({ ...map(), overrides: [map().overrides[0], map().overrides[0]] }), TypeError);
  assert.throws(() => chooseConfigurationAssignmentOverride(full, { cohortId: "typed group expression", displayName: "Invalid" }, release()), TypeError);
  for (const [field, value] of [["applicationId", binding], ["configurationId", "other"], ["schemaVersion", "1"], ["environment", "production"]]) {
    assert.throws(() => chooseConfigurationAssignmentDefault(full, { ...release(), [field]: value }), TypeError, field);
    assert.throws(() => chooseConfigurationAssignmentOverride(full, { cohortId: "cohort-0", displayName: "Existing" }, { ...release(), [field]: value }), TypeError, field);
  }
});

test("review accepts redacted whole-map metadata only after exact base and complete proposal echoes", () => {
  const draft = reason(removeConfigurationAssignmentOverride(chooseConfigurationAssignmentDefault(
    setConfigurationAssignmentIntent(beginConfigurationAssignment(map()), "promote"), release()), "canary"));
  const native = preview(draft, { redacted: true });
  const review = buildConfigurationAssignmentReview(draft, native);
  assert.equal(review.preview.redacted, true); assert.equal(review.command.reason, "  Exact reason é 日本語  ");
  assert.equal(review.rows.find(row => row.cohortId === "finance").change, "retained");
  native.currentAssignment.overrides[1].releaseId = first;
  native.recipients.items.push({ registrationId: binding, compatibility: "unknown" });
  assert.equal(review.baseline.overrides[1].releaseId, finance); assert.equal(review.preview.recipients.items.length, 0);
  assert.ok(Object.isFrozen(review.preview.currentAssignment.overrides));
  assert.ok(Object.isFrozen(review.command.overrides)); assert.ok(Object.isFrozen(review.rows));
});

test("same ETag cannot hide a changed full current map or reordered scope facts", () => {
  const draft = reason(beginConfigurationAssignment(map()));
  for (const change of [
    { eTag: '"changed"' }, { generation: "9007199254740994" }, { defaultReleaseId: candidate },
    { overrides: [{ cohortId: "canary", releaseId: first }, { cohortId: "finance", releaseId: finance }] },
    { overrides: [{ cohortId: "canary", releaseId: candidate }] }
  ]) {
    assert.throws(() => buildConfigurationAssignmentReview(draft, { ...preview(draft), currentAssignment: { ...map(), ...change } }), reviewError("map-changed"));
  }
  for (const [field, value] of [["applicationId", binding], ["configurationId", "other"], ["schemaVersion", "1"], ["environment", "production"], ["bindingId", app]]) {
    assert.throws(() => buildConfigurationAssignmentReview(draft, { ...preview(draft), currentAssignment: { ...map(), [field]: value } }), reviewError("scope-mismatch"));
    if (["applicationId", "configurationId", "schemaVersion"].includes(field)) {
      assert.throws(() => buildConfigurationAssignmentReview(draft, { ...preview(draft), [field]: value }), reviewError("scope-mismatch"));
    }
  }
  const reordered = preview(draft);
  reordered.currentAssignment.overrides.reverse();
  reordered.proposedAssignment = { ...reordered.proposedAssignment, overrides: [...reordered.proposedAssignment.overrides].reverse() };
  assert.equal(buildConfigurationAssignmentReview(draft, reordered).command.overrides[0].cohortId, "canary");
});

test("partial wrong or future proposals and structural refusals cannot become confirmable reviews", () => {
  const draft = reason(beginConfigurationAssignment(map()));
  const command = configurationAssignmentCommand(draft);
  for (const proposal of [{ ...command, intent: "future-intent" }, { ...command, reason: "trimmed or changed" },
    { ...command, defaultReleaseId: candidate }, { ...command, overrides: [command.overrides[0]] },
    { ...command, overrides: [command.overrides[0], command.overrides[0]] }, { ...command, commandVersion: 2 }]) {
    assert.throws(() => buildConfigurationAssignmentReview(draft, { ...preview(draft), proposedAssignment: proposal }), reviewError("proposal-mismatch"));
  }
  const sourcePair = { ...preview(draft), proposedAssignment: undefined, commonSource: { state: "absent" }, applicationSource: { state: "absent" } };
  assert.throws(() => buildConfigurationAssignmentReview(draft, sourcePair), reviewError("proposal-mismatch"));
  assert.throws(() => buildConfigurationAssignmentReview(draft, { ...preview(draft), structurallyValid: false }), reviewError("structural-refusal"));
  assert.throws(() => buildConfigurationAssignmentReview(draft, { ...preview(draft), issues: [{ code: "configuration.refused" }] }), reviewError("structural-refusal"));
  assert.throws(() => setConfigurationAssignmentIntent(draft, "future-intent"), TypeError);
});

test("reason text remains exact and bounded while incomplete drafts cannot create native commands", () => {
  const draft = beginConfigurationAssignment(map());
  assert.throws(() => configurationAssignmentCommand(draft), TypeError);
  const incomplete = setConfigurationAssignmentReason(draft, "   ");
  assert.equal(incomplete.reason, "   ");
  assert.throws(() => configurationAssignmentCommand(incomplete), TypeError);
  assert.equal(configurationAssignmentCommand(reason(draft)).reason, "  Exact reason é 日本語  ");
  assert.equal(configurationAssignmentCommand(setConfigurationAssignmentReason(draft, "é".repeat(512))).reason.length, 512);
  assert.throws(() => setConfigurationAssignmentReason(draft, "é".repeat(513)), TypeError);
  assert.throws(() => setConfigurationAssignmentReason(draft, "bad\nreason"), TypeError);
  assert.throws(() => beginConfigurationAssignment({ ...map(), generation: "9223372036854775808" }), TypeError);
  assert.throws(() => beginConfigurationAssignment({ ...map(), generation: 9007199254740993 }), TypeError);
});
