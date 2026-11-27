import assert from "node:assert/strict";
import test from "node:test";
import { adoptionPageMatches, adoptionPresentation, adoptionRowKey, freshnessLabel, projectConfigurationAdoptionPage,
  reportedStateLabel, updateModeLabel } from "../src/configuration-adoption-model.ts";

const applicationId = "11111111-1111-4111-8111-111111111111";
const releaseId = "22222222-2222-4222-8222-222222222222";
const previousReleaseId = "33333333-3333-4333-8333-333333333333";
const registrationId = "44444444-4444-4444-8444-444444444444";
function row(changes = {}) {
  return { applicationId, environment: "Development", configurationId: "country.settings", schemaVersion: "9007199254740993",
    registrationFreshness: "fresh", adoptionFreshness: "fresh", convergence: "applied", registrationId,
    reportedInstanceId: "worker-east", applicationVersion: "2.1.0", desired: { releaseId, generation: "9007199254740993" },
    reported: { configurationId: "country.settings", schemaVersion: "9007199254740993", updateMode: "HotReload", state: "Applied",
      liveStateValid: true, desired: { releaseId, generation: "9007199254740993" },
      applied: { releaseId, generation: "9007199254740993", localRevision: "0" },
      lastSuccessful: { releaseId: previousReleaseId, generation: "2", localRevision: "1" } },
    observedAtUtc: "2026-11-26T15:00:00.1234567Z", receivedAtUtc: "2026-11-26T15:00:01.7654321Z", ...changes };
}

test("adoption projection excludes compatible private additions at every layer and captures immutable exact provenance", () => {
  const original = row({ payloadJson: "private", principals: ["private"] });
  original.desired.secret = "private"; original.reported.applied.payloadJson = "private";
  original.reported.lastSuccessful.membership = "private"; original.reported.credentials = "private";
  const captured = projectConfigurationAdoptionPage({ items: [original], nextCursor: "opaque", privateMembership: "private" });
  assert.equal(JSON.stringify(captured).includes("private"), false);
  const result = captured.items[0];
  assert.equal(result.desired.generation, "9007199254740993"); assert.equal(result.reported.applied.localRevision, "0");
  assert.equal(result.reported.lastSuccessful.releaseId, previousReleaseId);
  assert.equal(result.observedAtUtc, "2026-11-26T15:00:00.1234567Z");
  assert.equal(result.receivedAtUtc, "2026-11-26T15:00:01.7654321Z");
  original.reported.applied.localRevision = "42"; original.desired.releaseId = previousReleaseId;
  assert.equal(result.reported.applied.localRevision, "0"); assert.equal(result.desired.releaseId, releaseId);
  assert.ok(Object.isFrozen(result.reported.lastSuccessful)); assert.ok(Object.isFrozen(result.desired));
  assert.ok(Object.isFrozen(captured.items)); assert.equal(captured.nextCursor, "opaque");
});

test("server desired, consumer desired, accepted and historical versions remain independently represented", () => {
  const original = row({ convergence: "pending" });
  original.reported.desired = { releaseId: previousReleaseId, generation: "2" };
  original.reported.applied = { releaseId: previousReleaseId, generation: "2", localRevision: "0" };
  const result = projectConfigurationAdoptionPage({ items: [original] }).items[0];
  assert.equal(result.desired.generation, "9007199254740993"); assert.equal(result.reported.desired.generation, "2");
  assert.equal(result.reported.applied.localRevision, "0");
  assert.equal(adoptionPresentation(result).label, "Pending current assignment");
  assert.notEqual(adoptionPresentation(result).tone, "good");
});

test("applied and unchanged are successful reported observations only with both fresh native facets", () => {
  assert.equal(adoptionPresentation(row()).label, "Applied (reported)"); assert.equal(adoptionPresentation(row()).tone, "good");
  const unchanged = row(); unchanged.reported.state = "AppliedUnchanged";
  assert.equal(adoptionPresentation(unchanged).label, "Applied unchanged (reported)");
  for (const facet of ["registrationFreshness", "adoptionFreshness"]) {
    for (const token of ["stale", "offline", "unknown", "futureFreshness"]) {
      const result = adoptionPresentation(row({ [facet]: token }));
      assert.equal(result.label, "Applied (last reported)"); assert.notEqual(result.tone, "good");
    }
  }
  assert.equal(freshnessLabel("offline"), "Offline"); assert.equal(freshnessLabel("fresh"), "Fresh");
});

test("pending restart, rejection and fault preserve their distinct meanings without historical fallback", () => {
  assert.equal(adoptionPresentation(row({ convergence: "pendingRestart" })).label, "Pending restart");
  for (const freshness of ["fresh", "stale", "offline"]) {
    const pending = adoptionPresentation(row({ convergence: "pendingRestart", registrationFreshness: freshness }));
    assert.match(pending.detail, /last report/u); assert.match(pending.detail, /unchanged then/u);
    assert.match(pending.detail, /Current live state is not guaranteed/u);
  }
  assert.equal(adoptionPresentation(row({ convergence: "rejected" })).label, "Rejected candidate");
  const faulted = row({ convergence: "faulted" }); faulted.reported.state = "Faulted";
  faulted.reported.liveStateValid = false; delete faulted.reported.applied;
  const captured = projectConfigurationAdoptionPage({ items: [faulted] }).items[0];
  assert.equal(captured.reported.applied, undefined); assert.equal(captured.reported.lastSuccessful.releaseId, previousReleaseId);
  assert.match(adoptionPresentation(captured).detail, /not guaranteed/u); assert.equal(adoptionPresentation(captured).tone, "warning");
});

test("missing report and native noInstances are distinct from an empty page and never adoption success", () => {
  const unreported = row({ convergence: "unknown", reported: undefined, adoptionFreshness: "unknown" });
  const none = { ...unreported, convergence: "noInstances", registrationId: undefined, reportedInstanceId: undefined,
    applicationVersion: undefined, registrationFreshness: "noInstances", adoptionFreshness: "noInstances" };
  assert.equal(adoptionPresentation(unreported).label, "Current adoption unknown");
  assert.equal(adoptionPresentation(none).label, "No observed recipients");
  assert.notEqual(adoptionPresentation(none).tone, "good");
  assert.deepEqual(projectConfigurationAdoptionPage({ items: [] }), { items: [] });
  assert.equal(projectConfigurationAdoptionPage({ items: [none] }).items.length, 1);
});

test("a historical Faulted report cannot replace native unknown convergence for an ineligible target", () => {
  const ineligible = row({ convergence: "unknown" });
  ineligible.reported.state = "Faulted"; ineligible.reported.liveStateValid = false; delete ineligible.reported.applied;
  const captured = projectConfigurationAdoptionPage({ items: [ineligible] }).items[0];
  assert.equal(adoptionPresentation(captured).label, "Current adoption unknown");
  assert.equal(adoptionPresentation(captured).tone, "neutral");
  assert.equal(reportedStateLabel(captured.reported.state), "Faulted");
  assert.equal(captured.reported.lastSuccessful.releaseId, previousReleaseId);
  assert.equal(captured.reported.applied, undefined);
});

test("an earlier page cannot present old fresh facets as current success or rewrite the native observations", () => {
  for (const convergence of ["applied", "pending", "pendingRestart", "rejected", "faulted", "unknown", "noInstances"]) {
    const observed = row({ convergence });
    const presentation = adoptionPresentation(observed, true);
    assert.equal(presentation.tone, "neutral"); assert.match(presentation.label, /last observed|earlier page/u);
    assert.match(presentation.detail, /could not be refreshed/u);
    assert.equal(observed.registrationFreshness, "fresh"); assert.equal(observed.adoptionFreshness, "fresh");
    assert.equal(observed.desired.generation, "9007199254740993");
  }
  const previous = row(); previous.reported.state = "AppliedUnchanged";
  assert.equal(adoptionPresentation(previous, true).label, "Applied unchanged (last observed)");
  assert.equal(adoptionPresentation(previous).label, "Applied unchanged (reported)");
});

test("future native tokens remain inert unknowns while exact raw tokens survive only as observations", () => {
  const future = row({ convergence: "futureApplied" }); future.reported.state = "FutureApplied";
  const captured = projectConfigurationAdoptionPage({ items: [future] }).items[0];
  assert.equal(captured.convergence, "futureApplied"); assert.equal(captured.reported.state, "FutureApplied");
  assert.equal(adoptionPresentation(captured).label, "Current adoption unknown"); assert.notEqual(adoptionPresentation(captured).tone, "good");
  assert.equal(reportedStateLabel("FutureApplied"), "Unknown reported state");
  const futureMode = row(); futureMode.reported.updateMode = "FutureMode";
  assert.equal(adoptionPresentation(futureMode).label, "Current adoption unknown");
  assert.equal(updateModeLabel("FutureMode"), "Unknown update mode"); assert.equal(freshnessLabel("futureFreshness"), "Unknown freshness");
});

test("a future report state or mode prevents known convergence tokens from implying recognized candidate or live semantics", () => {
  for (const convergence of ["pendingRestart", "rejected", "faulted", "pending", "applied"]) {
    for (const field of ["state", "updateMode"]) {
      const future = row({ convergence });
      future.reported[field] = field === "state" ? "FutureApplied" : "FutureMode";
      const captured = projectConfigurationAdoptionPage({ items: [future] }).items[0];
      const presentation = adoptionPresentation(captured);
      assert.equal(presentation.label, "Current adoption unknown"); assert.equal(presentation.tone, "neutral");
      assert.match(presentation.detail, /unrecognized/u);
      assert.equal(captured.reported[field], future.reported[field]);
      assert.equal(captured.reported.lastSuccessful.releaseId, previousReleaseId);
      assert.equal(captured.desired.generation, "9007199254740993");
    }
  }
});

test("registration and cohort identity select rows even when reported instance labels are identical", () => {
  const first = row(); const second = row({ registrationId: "55555555-5555-4555-8555-555555555555" });
  const cohort = row({ cohortId: "finance" });
  assert.equal(first.reportedInstanceId, second.reportedInstanceId);
  assert.notEqual(adoptionRowKey(first), adoptionRowKey(second)); assert.notEqual(adoptionRowKey(first), adoptionRowKey(cohort));
  assert.notEqual(adoptionRowKey(row({ registrationId: undefined })), adoptionRowKey(row({ registrationId: undefined, cohortId: "finance" })));
});

test("incoming pages must retain the complete selected tuple and unique native recipient keys", () => {
  const matching = { items: [row()] };
  assert.equal(adoptionPageMatches(matching, applicationId, "country.settings", "9007199254740993"), true);
  assert.equal(adoptionPageMatches({ items: [] }, applicationId, "country.settings", "9007199254740993"), true);
  for (const change of [{ applicationId: "66666666-6666-4666-8666-666666666666" }, { configurationId: "other.settings" }, { schemaVersion: "2" }]) {
    assert.equal(adoptionPageMatches({ items: [row(change)] }, applicationId, "country.settings", "9007199254740993"), false);
  }
  const nested = row(); nested.reported.schemaVersion = "2";
  assert.equal(adoptionPageMatches({ items: [nested] }, applicationId, "country.settings", "9007199254740993"), false);
  assert.equal(adoptionPageMatches({ items: [row(), row()] }, applicationId, "country.settings", "9007199254740993"), false);
  assert.equal(adoptionPageMatches({ items: [row(), row({ registrationId: undefined, environment: "Production" })] }, applicationId, "country.settings", "9007199254740993"), false);
});
