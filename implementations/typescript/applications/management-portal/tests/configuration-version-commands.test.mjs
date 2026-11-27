import assert from "node:assert/strict";
import test from "node:test";
import { acceptConfigurationVersionSavePreview, captureConfigurationVersionSave, configurationVersionDeployCommand,
  configurationVersionSaveCandidate, configurationVersionSaveMatches, executeConfigurationVersionSave,
  ConfigurationVersionSaveInterrupted, reviewConfigurationVersionDeploy } from "../src/configuration-version-commands.ts";
import { acceptConfigurationVersionReleasePreview, configurationVersionReleasePreview, rebindConfigurationVersionRelease } from "../src/configuration-version-commands.ts";

const app = "11111111-1111-4111-8111-111111111111", binding = "22222222-2222-4222-8222-222222222222";
const source = "33333333-3333-4333-8333-333333333333", common = "44444444-4444-4444-8444-444444444444";
const commonPublication = "55555555-5555-4555-8555-555555555555", appPublication = "66666666-6666-4666-8666-666666666666";
const releaseId = "77777777-7777-4777-8777-777777777777", other = "88888888-8888-4888-8888-888888888888";
const raw = String.raw` {"Large":9007199254740993,"Exponent":1.2300e+40,"Unicode":"\u65e5 日本語 🌍","False":false,"Zero":-0,"Null":null} `;
function map() { return { bindingId: binding, applicationId: app, environment: "Development", configurationId: "settings", schemaVersion: "1",
  eTag: '"map.5"', defaultReleaseId: other, generation: "9007199254740993", overrides: [{ cohortId: "finance", releaseId: other }] }; }
function base() { return { releaseId, applicationId: app, configurationId: "settings", schemaVersion: "1", environment: "Development",
  commonSource: { state: "present", publicationId: commonPublication }, applicationSource: { state: "absent" },
  actor: "Operator:opaque", createdAtUtc: "2026-11-26T12:00:00.1234567Z", reason: "Exact older Shared base", eTag: '"release"' }; }
function editor() { return { context: { applicationId: app, application: "CountryData", environment: "Development", bindingId: binding,
  configurationId: "settings", schemaVersion: "1", hostApplicationName: "Settings", selectedSource: "application",
  applicationSource: { state: "available", sourceId: source }, commonSource: { state: "available", sourceId: common },
  shape: { kind: "object", nullable: false, members: [] }, defaults: { state: "available", defaultsJson: "{}" } },
  source: { sourceId: source, applicationId: app, environment: "Development", configurationId: "settings", schemaVersion: "1", kind: "application",
    eTag: '"source.2"', publications: { items: [] } }, assignment: map(), content: { state: "absent" }, actions: { state: "available",
    source: { resourceId: source, actions: ["publishConfigurationSource", "validateConfiguration"].map(operationId => ({ operationId, supported: true, allowed: true })) },
    binding: { resourceId: binding, actions: ["createConfigurationRelease", "previewConfiguration"].map(operationId => ({ operationId, supported: true, allowed: true })) } } }; }
const capture = (value = editor(), selected = base()) => captureConfigurationVersionSave(value, raw, "  Regional settings  ", "current-session", selected);
function preview(intent) { return { ...configurationVersionSaveCandidate(intent), bindingId: binding, environment: "Development", hostApplicationName: "Settings",
  assignmentETag: intent.assignmentETag, draftSha256: intent.sha256, structurallyValid: true, redacted: false, issues: [], changes: [],
  currentAssignment: intent.baseline, recipients: { items: [] } }; }
function publication(intent, changes = {}) { return { family: "management.configuration.source.publish", key: "publication-original", target: { sourceId: source },
  disposition: "applied", originalCompletion: "committed", resultSource: "prepared", decision: { kind: "sourcePublished", decision: "applied", sourceId: source,
    publicationId: appPublication, sha256: intent.sha256 }, transport: { kind: "http", status: 200, cleanupSucceeded: true, mutationUncertain: false }, ...changes }; }
function release(changes = {}) { return { family: "management.configuration.release.create", key: "release-original", target: { applicationId: app, configurationId: "settings", schemaVersion: "1" },
  disposition: "applied", originalCompletion: "committed", resultSource: "prepared", decision: { kind: "releaseCreated", decision: "applied", applicationId: app,
    configurationId: "settings", schemaVersion: "1", releaseId }, transport: { kind: "http", status: 200, cleanupSucceeded: true, mutationUncertain: false }, ...changes }; }

test("Save captures exact raw JSON and selected old Shared base, never defaults/history, with detached current map and untrimmed note", async () => {
  const value = editor(), selected = base(), intent = await capture(value, selected);
  assert.equal(intent.raw, raw); assert.equal(intent.note, "  Regional settings  ");
  assert.equal(intent.commonSource.publicationId, commonPublication); assert.equal(intent.baseline.generation, "9007199254740993");
  value.assignment.overrides[0].releaseId = releaseId; selected.commonSource.publicationId = other;
  assert.equal(intent.commonSource.publicationId, commonPublication); assert.equal(intent.baseline.overrides[0].releaseId, other);
  assert.ok(Object.isFrozen(intent));
  assert.deepEqual((await capture(editor(), "application-only")).commonSource, { state: "absent" });
});

test("Save admission refuses foreign scopes, borrowed action resources, malformed JSON and canceled capture before any transport", async () => {
  for (const modify of [value => { value.assignment.bindingId = other; }, value => { value.context.defaults = { state: "redacted" }; },
    value => { value.source.applicationId = other; }, value => { value.actions.source.resourceId = common; },
    value => { value.actions.binding.actions[0].allowed = false; }]) {
    const value = editor(); modify(value); await assert.rejects(capture(value), TypeError);
  }
  await assert.rejects(capture(editor(), { ...base(), applicationId: other }), TypeError);
  await assert.rejects(captureConfigurationVersionSave(editor(), '{"x":1,"x":2}', "Reason", "session", base()));
  const controller = new AbortController(); controller.abort();
  await assert.rejects(captureConfigurationVersionSave(editor(), raw, "Reason", "session", base(), controller.signal));
});

test("native preview must echo the complete current map, exact references, source/preconditions and raw SHA before publication", async () => {
  const intent = await capture(); acceptConfigurationVersionSavePreview(intent, preview(intent));
  for (const changed of [{ ...preview(intent), draftSha256: "f".repeat(64) }, { ...preview(intent), commonSource: { state: "absent" } },
    { ...preview(intent), currentAssignment: { ...intent.baseline, overrides: [] } }, { ...preview(intent), sourceId: common },
    { ...preview(intent), redacted: true }, { ...preview(intent), issues: [{ code: "incomplete" }] }]) {
    assert.throws(() => acceptConfigurationVersionSavePreview(intent, changed), TypeError);
  }
  assert.equal(configurationVersionSaveMatches(intent, editor(), "current-session"), true);
  assert.equal(configurationVersionSaveMatches(intent, editor(), "changed-session"), false);
  const changed = editor(); changed.source.eTag = '"source.after-own-publication"';
  assert.equal(configurationVersionSaveMatches(intent, changed, "current-session", "publication"), false);
  assert.equal(configurationVersionSaveMatches(intent, changed, "current-session", "release"), true);
  changed.assignment.overrides = []; assert.equal(configurationVersionSaveMatches(intent, changed, "current-session", "release"), false);
});

test("one Save awaits preview then publication cleanup before the distinct release and never submits assignment", async () => {
  const intent = await capture(), calls = [], entered = deferred(), held = deferred();
  const pending = executeConfigurationVersionSave(intent, { isCurrent: () => true,
    preview: async command => { calls.push("preview"); assert.equal(command.payloadJson, raw); return preview(intent); },
    publish: async (command, eTag) => { calls.push("publication"); assert.equal(command.payloadJson, raw); assert.equal(eTag, intent.sourceETag); entered.resolve(); await held.promise; return publication(intent); },
    release: async (command, eTag) => { calls.push("release"); assert.equal(eTag, intent.assignmentETag); assert.deepEqual(command.commonSource, intent.commonSource);
      assert.deepEqual(command.applicationSource, { state: "present", publicationId: appPublication }); return release(); } });
  try { await entered.promise; assert.deepEqual(calls, ["preview", "publication"]); }
  finally { held.resolve(); }
  const result = await pending; assert.equal(result.stage, "saved"); assert.equal(result.savedReleaseId, releaseId);
  assert.deepEqual(calls, ["preview", "publication", "release"]); assert.notEqual(result.publication.key, result.release.key);
});

test("unknown publication, failed cleanup or changed session stops later stages and retains actual earlier facts", async () => {
  const intent = await capture();
  for (const value of [publication(intent, { disposition: "unresolved", originalCompletion: "unknown" }),
    publication(intent, { transport: { kind: "http", status: 200, cleanupSucceeded: false, mutationUncertain: false } }),
    publication(intent, { decision: { ...publication(intent).decision, sha256: "f".repeat(64) } })]) {
    let releases = 0;
    const result = await executeConfigurationVersionSave(intent, { isCurrent: () => true, preview: async () => preview(intent), publish: async () => value,
      release: async () => { releases++; return release(); } });
    assert.equal(releases, 0); assert.equal(result.publication, value); assert.equal(result.savedReleaseId, undefined);
  }
  let current = true, releases = 0;
  const result = await executeConfigurationVersionSave(intent, { isCurrent: () => current, preview: async () => preview(intent),
    publish: async () => { current = false; return publication(intent); }, release: async () => { releases++; return release(); } });
  assert.equal(result.stage, "release"); assert.equal(releases, 0); assert.equal(result.publication.originalCompletion, "committed");
});

test("explicit Continue reuses only the exact known publication; a release fault preserves it without automatic replay", async () => {
  const intent = await capture(), known = publication(intent), cause = new Error("Owned release request did not confirm");
  let published = 0, previews = 0, releases = 0;
  const ports = { isCurrent: () => true, preview: async () => { previews++; return preview(intent); },
    publish: async () => { published++; return known; }, release: async () => { releases++; throw cause; } };
  await assert.rejects(executeConfigurationVersionSave(intent, ports, known), error => {
    assert.ok(error instanceof ConfigurationVersionSaveInterrupted); assert.equal(error.cause, cause);
    assert.equal(error.progress.stage, "release"); assert.equal(error.progress.publication, known); return true;
  });
  assert.equal(previews, 0); assert.equal(published, 0); assert.equal(releases, 1);
});

test("a known saved release survives its independent cleanup warning while a foreign release result is never Saved", async () => {
  const intent = await capture();
  for (const value of [release(), release({ transport: { kind: "http", status: 503, cleanupSucceeded: false, mutationUncertain: false } })]) {
    const result = await executeConfigurationVersionSave(intent, { isCurrent: () => true, preview: async () => preview(intent), publish: async () => publication(intent), release: async () => value });
    assert.equal(result.stage, "saved"); assert.equal(result.release, value); assert.equal(result.savedReleaseId, releaseId);
  }
  const foreign = release({ target: { applicationId: other, configurationId: "settings", schemaVersion: "1" } });
  const result = await executeConfigurationVersionSave(intent, { isCurrent: () => true, preview: async () => preview(intent), publish: async () => publication(intent), release: async () => foreign });
  assert.equal(result.stage, "release"); assert.equal(result.savedReleaseId, undefined);
});

test("Deploy older version deliberately removes every existing pin and accepts only its exact full-map native preview", () => {
  const baseline = map(), version = base(), command = configurationVersionDeployCommand(baseline, version, "Deploy baseline to all instances");
  assert.equal(command.defaultReleaseId, releaseId); assert.equal(command.intent, "replace"); assert.deepEqual(command.overrides, []);
  assert.equal(baseline.overrides.length, 1); assert.equal(baseline.generation, "9007199254740993");
  const value = { applicationId: app, configurationId: "settings", schemaVersion: "1", currentAssignment: baseline, proposedAssignment: command,
    structurallyValid: true, redacted: true, changes: [], issues: [], recipients: { items: [] } };
  const review = reviewConfigurationVersionDeploy(baseline, version, command.reason, value);
  assert.equal(review.preview.redacted, true); assert.equal(review.rows.find(row => row.cohortId === "finance").change, "removed");
  assert.throws(() => reviewConfigurationVersionDeploy(baseline, version, command.reason, { ...value, proposedAssignment: { ...command, overrides: baseline.overrides } }));
  assert.throws(() => reviewConfigurationVersionDeploy(baseline, version, command.reason, { ...value, currentAssignment: { ...baseline, eTag: '"raced"' } }));
  assert.throws(() => configurationVersionDeployCommand(baseline, { ...version, applicationId: other }, command.reason));
});

test("explicit resolved-release reconciliation preserves the exact already-published version and requires fresh pair/map preview without re-publication", async () => {
  const intent = await capture(), known = publication(intent);
  const rejected = release({ disposition: "conflict", decision: { ...release().decision, decision: "rejected", code: "management.configuration.precondition_failed" } });
  const fresh = editor(); fresh.assignment.eTag = '"map.6"'; fresh.assignment.generation = "9007199254740994";
  fresh.actions.source.actions.forEach(action => { action.allowed = false; });
  const rebound = rebindConfigurationVersionRelease(intent, fresh, intent.sessionKey, known, rejected, rejected.key);
  assert.equal(rebound.raw, intent.raw); assert.equal(rebound.sha256, intent.sha256); assert.equal(rebound.note, intent.note);
  assert.deepEqual(rebound.commonSource, intent.commonSource); assert.equal(rebound.assignmentETag, fresh.assignment.eTag);
  const command = configurationVersionReleasePreview(rebound, known);
  assert.equal(command.applicationSource.publicationId, appPublication); assert.equal(command.commonSource.publicationId, commonPublication);
  const checked = { applicationId: app, configurationId: "settings", schemaVersion: "1", commonSource: command.commonSource, applicationSource: command.applicationSource,
    currentAssignment: rebound.baseline, structurallyValid: true, redacted: true, issues: [], changes: [], recipients: { items: [] } };
  acceptConfigurationVersionReleasePreview(rebound, known, checked);
  assert.throws(() => acceptConfigurationVersionReleasePreview(rebound, known, { ...checked, applicationSource: { state: "absent" } }), TypeError);
  assert.throws(() => acceptConfigurationVersionReleasePreview(rebound, known, { ...checked, currentAssignment: intent.baseline }), TypeError);
});

test("unknown unresolved or contradictory release outcomes never authorize a new release key or cross-session rebind", async () => {
  const intent = await capture(), known = publication(intent), rejected = release({ disposition: "rejected" });
  for (const closed of [release({ disposition: "unresolved", originalCompletion: "unknown" }), release(),
    { ...rejected, target: { applicationId: other, configurationId: "settings", schemaVersion: "1" } },
    { ...rejected, recovery: { state: "conflicting" } }]) {
    assert.throws(() => rebindConfigurationVersionRelease(intent, editor(), intent.sessionKey, known, closed, closed.key), TypeError);
  }
  assert.throws(() => rebindConfigurationVersionRelease(intent, editor(), "another-session", known, rejected, rejected.key), TypeError);
  assert.throws(() => rebindConfigurationVersionRelease(intent, editor(), intent.sessionKey, known, rejected, "not-the-dismissed-key"), TypeError);
});

test("currentness loss during held preview or release joins the callback without advancing or hiding known original facts", async () => {
  for (const heldStage of ["preview", "release"]) {
    const intent = await capture(), entered = deferred(), finished = deferred(), calls = [];
    const published = publication(intent), released = release();
    let current = true, settled = false;
    async function stage(name, value) {
      calls.push(name);
      if (name === heldStage) { entered.resolve(); await finished.promise; }
      return value;
    }
    const pending = executeConfigurationVersionSave(intent, {
      isCurrent: () => current,
      preview: async () => stage("preview", preview(intent)),
      publish: async () => stage("publication", published),
      release: async () => stage("release", released)
    });
    void pending.then(() => { settled = true; }, () => { settled = true; });
    await entered.promise;
    try {
      current = false;
      await Promise.resolve();
      assert.equal(settled, false, "A changed session or target cannot manufacture settlement of admitted native work.");
    } finally { finished.resolve(); }
    const result = await pending;
    assert.equal(result.savedReleaseId, undefined);
    if (heldStage === "preview") {
      assert.deepEqual(calls, ["preview"]);
      assert.equal(result.publication, undefined); assert.equal(result.release, undefined);
    } else {
      assert.deepEqual(calls, ["preview", "publication", "release"]);
      assert.equal(result.stage, "release");
      assert.equal(result.publication, published); assert.equal(result.release, released);
      assert.equal(result.release.originalCompletion, "committed", "Late currentness loss does not rewrite an actual committed original as unknown or rolled back.");
    }
  }
});

test("foreign native preview tuple or source/map preconditions prevent every publication and release port", async () => {
  for (const mutate of [value => { value.environment = "Production"; }, value => { value.bindingId = other; },
    value => { value.hostApplicationName = "OtherSettings"; }, value => { value.sourceETag = '"source.changed"'; },
    value => { value.assignmentETag = '"map.changed"'; }, value => { value.applicationSource = { state: "present", publicationId: other }; }]) {
    const intent = await capture(), nativePreview = preview(intent), effects = [];
    mutate(nativePreview);
    await assert.rejects(executeConfigurationVersionSave(intent, {
      isCurrent: () => true,
      preview: async () => nativePreview,
      publish: async () => { effects.push("publication"); return publication(intent); },
      release: async () => { effects.push("release"); return release(); }
    }), error => error instanceof ConfigurationVersionSaveInterrupted && error.cause instanceof TypeError
      && error.progress.stage === "preview" && error.progress.publication === undefined && error.progress.release === undefined);
    assert.deepEqual(effects, [], "A valid JSON payload cannot authorize a Save under another binding, scope or stale source/map precondition.");
  }
});

function deferred() { let resolve; const promise = new Promise(done => { resolve = done; }); return { promise, resolve }; }
