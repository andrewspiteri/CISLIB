import assert from "node:assert/strict";
import test from "node:test";
import { instanceConfigurationVersion, instanceStatusPresentation } from "../src/configuration-instance-status-model.ts";

const applicationId = "11111111-1111-4111-8111-111111111111", releaseId = "22222222-2222-4222-8222-222222222222";
const olderRelease = "33333333-3333-4333-8333-333333333333", foreign = "44444444-4444-4444-8444-444444444444";
const generation = "9007199254740993";
function row() {
  return { applicationId, environment: "Development", configurationId: "country.settings", schemaVersion: "1",
    registrationId: "55555555-5555-4555-8555-555555555555", reportedInstanceId: "country-api-01", applicationVersion: "4.2.0",
    registrationFreshness: "fresh", adoptionFreshness: "fresh", convergence: "applied", desired: { releaseId, generation },
    reported: { configurationId: "country.settings", schemaVersion: "1", updateMode: "HotReload", state: "Applied", liveStateValid: true,
      desired: { releaseId, generation }, applied: { releaseId, generation, localRevision: "0" },
      lastSuccessful: { releaseId: olderRelease, generation: "1", localRevision: "1" } } };
}
function release(id = releaseId) {
  return { releaseId: id, applicationId, environment: "Development", configurationId: "country.settings", schemaVersion: "1",
    commonSource: { state: "absent" }, applicationSource: { state: "absent" }, reason: "European catalogue", actor: "operator:opaque",
    createdAtUtc: "2026-11-26T20:00:00.1234567Z", eTag: '"release.1"' };
}

test("Up to date requires fresh recognized exact desired/report/applied release and generation, with no numeric coercion", () => {
  assert.equal(instanceStatusPresentation(row()).label, "Up to date");
  const unchanged = row(); unchanged.reported.state = "AppliedUnchanged";
  assert.equal(instanceStatusPresentation(unchanged).tone, "good");
  for (const mutate of [value => { value.reported.applied.generation = "9007199254740992"; },
    value => { value.reported.desired.generation = "9007199254740992"; }, value => { value.reported.applied.releaseId = olderRelease; },
    value => { delete value.desired; }, value => { value.reported.liveStateValid = false; }, value => { delete value.reported.applied; }]) {
    const value = row(); mutate(value);
    const result = instanceStatusPresentation(value);
    assert.equal(result.label, "Unknown"); assert.equal(result.tone, "neutral");
  }
  const rollback = row(); rollback.convergence = "pending"; rollback.desired.generation = "3";
  rollback.reported.desired.generation = "1"; rollback.reported.applied.generation = "1";
  assert.equal(instanceStatusPresentation(rollback).label, "Pending", "The same immutable release at a later assignment generation is not an inferred acknowledgment.");
});

test("recognized restart/rejection/fault remain distinct from current accepted-version and notification failure", () => {
  const restart = row(); restart.convergence = "pendingRestart"; restart.reported.state = "PendingRestart"; restart.reported.updateMode = "RestartRequired";
  restart.reported.applied.releaseId = olderRelease;
  assert.equal(instanceStatusPresentation(restart).label, "Needs restart");
  assert.equal(instanceConfigurationVersion(restart, [release(olderRelease)]).releaseId, olderRelease);
  for (const [convergence, state] of [["rejected", "Rejected"], ["faulted", "Faulted"]]) {
    const value = row(); value.convergence = convergence; value.reported.state = state;
    assert.equal(instanceStatusPresentation(value).label, "Failed"); assert.equal(instanceStatusPresentation(value).tone, "warning");
  }
  for (const convergence of ["pendingRestart", "rejected", "faulted"]) {
    const contradictory = row(); contradictory.convergence = convergence;
    assert.equal(instanceStatusPresentation(contradictory).label, "Unknown", "A known but contradictory Applied report cannot establish restart/rejection/fault.");
    assert.equal(instanceStatusPresentation(contradictory).tone, "neutral");
  }
  const notification = row(); notification.reported.notificationFailureCode = "observer.failed";
  assert.equal(instanceStatusPresentation(notification).label, "Up to date", "A notification failure is not failed Configuration acceptance.");
});

test("stale offline earlier missing future and no-instance observations never become green success or fabricated instances", () => {
  for (const facet of ["registrationFreshness", "adoptionFreshness"]) {
    for (const value of ["stale", "offline", "unknown", "futureFreshness"]) {
      const record = row(); record[facet] = value;
      assert.equal(instanceStatusPresentation(record).label, "Unknown"); assert.equal(instanceStatusPresentation(record).tone, "neutral");
    }
  }
  for (const field of ["state", "updateMode"]) {
    const value = row(); value.reported[field] = "futureToken";
    assert.equal(instanceStatusPresentation(value).label, "Unknown");
    assert.equal(instanceConfigurationVersion(value, [release()]).state, "unknown");
  }
  const historicalFault = row(); historicalFault.convergence = "unknown"; historicalFault.reported.state = "Faulted"; historicalFault.reported.liveStateValid = false;
  assert.equal(instanceStatusPresentation(historicalFault).label, "Unknown", "Historical report state cannot replace native unknown convergence.");
  const missing = row(); delete missing.reported;
  assert.equal(instanceStatusPresentation(missing).label, "Unknown");
  assert.equal(instanceConfigurationVersion(missing, [release()]).label, "Not reported");
  assert.equal(instanceStatusPresentation(row(), true).label, "Unknown");
  const none = { ...missing, convergence: "noInstances", registrationId: undefined, reportedInstanceId: undefined };
  assert.equal(instanceStatusPresentation(none).label, "No observed instances"); assert.equal(instanceStatusPresentation(none).tone, "neutral");
  const fakeAcceptedTarget = row(); fakeAcceptedTarget.convergence = "noInstances"; delete fakeAcceptedTarget.registrationId;
  assert.equal(instanceConfigurationVersion(fakeAcceptedTarget, [release()]).label, "Not reported", "A target without instances never borrows an accepted version.");
});

test("version uses only recognized valid reported applied identity, never app version desired generation or historical success", () => {
  const value = row(); value.convergence = "pending"; value.reported.applied.releaseId = olderRelease;
  const before = JSON.stringify(value);
  const accepted = instanceConfigurationVersion(value, [release(), { ...release(olderRelease), reason: "Older accepted catalogue" }]);
  assert.equal(accepted.label, "Older accepted catalogue"); assert.equal(accepted.releaseId, olderRelease);
  assert.equal(accepted.createdAtUtc, "2026-11-26T20:00:00.1234567Z");
  assert.equal(JSON.stringify(accepted).includes(value.applicationVersion), false);
  delete value.reported.applied;
  assert.equal(instanceConfigurationVersion(value, [release(), release(olderRelease)]).label, "Not reported");
  value.reported.liveStateValid = false;
  value.reported.state = "Faulted"; value.convergence = "faulted";
  assert.equal(instanceConfigurationVersion(value, [release(olderRelease)]).label, "Not reported", "Canonical Faulted with no applied value cannot borrow historical success.");
  value.reported.applied = { releaseId: olderRelease, generation: "1", localRevision: "0" };
  assert.equal(instanceConfigurationVersion(value, [release(olderRelease)]).label, "Current version unknown", "An applied value with invalid live state is not an in-use claim.");
  assert.ok(before.includes(generation), "The fixture retains an exact large assignment generation independently of its label.");
});

test("optional authorized metadata requires exact release/app/config/schema/environment and never hides a status row", () => {
  const value = row(), original = JSON.stringify(value);
  for (const metadata of [undefined, [], [release(olderRelease)], [{ ...release(), applicationId: foreign }], [{ ...release(), configurationId: "other" }],
    [{ ...release(), schemaVersion: "2" }], [{ ...release(), environment: "Production" }],
    [release(), { ...release(), reason: "Contradictory note" }], Array.from({ length: 102 }, () => release())]) {
    const displayed = instanceConfigurationVersion(value, metadata);
    assert.equal(displayed.label, "Version details unavailable"); assert.equal(displayed.releaseId, releaseId);
    assert.equal(displayed.label.includes(releaseId), false); assert.equal(instanceStatusPresentation(value).label, "Up to date");
  }
  const metadata = { ...release(), privateToken: "secret", payloadJson: "secret", actor: "private-actor" };
  const displayed = instanceConfigurationVersion(value, [metadata, { ...metadata }]);
  assert.equal(displayed.label, "European catalogue"); assert.equal(JSON.stringify(displayed).includes("secret"), false);
  assert.equal(JSON.stringify(displayed).includes("private-actor"), false); assert.ok(Object.isFrozen(displayed));
  assert.equal(JSON.stringify(value), original, "Display-only metadata cannot mutate or discard the authorized observation.");
  assert.equal(instanceConfigurationVersion(value, [{ ...release(), reason: "" }]).label, "Saved 2026-11-26T20:00:00.1234567Z");
});

test("dated failure and restart observations stay neutral while independently valid accepted versions retain their own meaning", () => {
  const cases = [
    { convergence: "faulted", state: "Faulted", registrationFreshness: "offline", adoptionFreshness: "fresh", liveStateValid: false, version: "unknown" },
    { convergence: "pendingRestart", state: "PendingRestart", registrationFreshness: "fresh", adoptionFreshness: "stale", liveStateValid: true, version: "available" },
    { convergence: "rejected", state: "Rejected", registrationFreshness: "stale", adoptionFreshness: "fresh", liveStateValid: true, version: "available" },
    { convergence: "faulted", state: "Faulted", registrationFreshness: "fresh", adoptionFreshness: "fresh", liveStateValid: true,
      updateMode: "FutureUpdateMode", version: "unknown" }
  ];
  for (const example of cases) {
    const value = row();
    value.convergence = example.convergence;
    value.registrationFreshness = example.registrationFreshness; value.adoptionFreshness = example.adoptionFreshness;
    value.reported.state = example.state; value.reported.liveStateValid = example.liveStateValid;
    value.reported.applied.releaseId = olderRelease;
    if (example.updateMode !== undefined) { value.reported.updateMode = example.updateMode; }
    const before = JSON.stringify(value), status = instanceStatusPresentation(value);
    assert.equal(status.label, "Unknown"); assert.equal(status.tone, "neutral");
    const version = instanceConfigurationVersion(value, [release(olderRelease)]);
    assert.equal(version.state, example.version);
    if (example.version === "available") { assert.equal(version.releaseId, olderRelease); }
    assert.equal(JSON.stringify(value), before, "Freshness qualification changes presentation, not the reported or historical native facts.");
  }
});
