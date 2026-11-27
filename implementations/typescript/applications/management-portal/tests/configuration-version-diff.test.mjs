import assert from "node:assert/strict";
import test from "node:test";
import { compareConfigurationSavedVersions, compareConfigurationVersionJson, compareWithDeployedConfiguration } from "../src/configuration-version-diff.ts";
import { configurationVersionDeployment, configurationVersionLabel, projectConfigurationVersion } from "../src/configuration-versions.ts";

const app = "11111111-1111-4111-8111-111111111111", releaseId = "22222222-2222-4222-8222-222222222222";
const other = "33333333-3333-4333-8333-333333333333", publicationId = "44444444-4444-4444-8444-444444444444";
const raw = String.raw`{"Large":9007199254740993,"Exponent":1.2300e+40,"Unicode":"\u65e5 日本語 🌍","False":false,"Zero":-0,"Null":null}`;
function version() { return { release: { releaseId, applicationId: app, configurationId: "settings", schemaVersion: "1", environment: "Development",
  commonSource: { state: "absent" }, applicationSource: { state: "present", publicationId }, actor: "Operator:opaque", reason: "Regional settings",
  createdAtUtc: "2026-11-26T12:34:56.1234567Z", eTag: '"release"' }, content: { state: "present", layer: "application", publication: {
    publicationId, sourceId: other, configurationId: "settings", schemaVersion: "1", environment: "Development", kind: "application", applicationId: app,
    sha256: "a".repeat(64), payloadJson: raw, actor: "Operator:opaque", reason: "Regional settings", createdAtUtc: "2026-11-26T12:34:56.0000001Z" } } }; }
function map() { return { bindingId: "55555555-5555-4555-8555-555555555555", applicationId: app, configurationId: "settings", schemaVersion: "1",
  environment: "Development", eTag: '"map"', generation: "9007199254740993", defaultReleaseId: releaseId, overrides: [] }; }

test("exact text comparison preserves large numbers exponent spelling escaped Unicode false null and signed zero", () => {
  const changed = raw.replace("9007199254740993", "9007199254740995");
  const diff = compareConfigurationVersionJson(raw, changed);
  assert.equal(diff.state, "ready"); assert.equal(diff.changed, true);
  assert.ok(diff.rows.some(row => row.left?.includes("9007199254740993") && row.right?.includes("9007199254740995") && row.kind === "changed"));
  const before = diff.rows.map(row => row.left ?? "").join("\n");
  for (const token of ["1.2300e+40", String.raw`\u65e5`, "日本語 🌍", "false", "null", "-0"]) { assert.ok(before.includes(token)); }
  assert.equal(compareConfigurationVersionJson(raw, " \n " + raw + "\t").changed, false);
  assert.equal(compareConfigurationVersionJson(raw, raw.replace("1.2300e+40", "12300e+36")).changed, true, "Lexical numeric changes are not coerced through Number.");
});

test("malformed duplicate-member and oversized comparisons are refused rather than normalized or silently truncated", () => {
  for (const bad of ['{"x":1,"x":2}', '{"x":"\ud800"}', '{"x":', '\ufeff{}']) {
    assert.equal(compareConfigurationVersionJson(raw, bad).state, "unavailable");
  }
  const many = '{"Rows":[' + "0,".repeat(600) + "0]}";
  assert.equal(compareConfigurationVersionJson(many, many).state, "unavailable");
  const huge = '{"Text":"' + "x".repeat(262144) + '"}';
  assert.equal(compareConfigurationVersionJson(raw, huge).state, "unavailable");
});

test("identical Application JSON cannot claim unchanged saved versions when Shared bases or references differ", () => {
  const left = version(), right = version();
  right.release.releaseId = other; right.release.commonSource = { state: "present", publicationId: other };
  const diff = compareConfigurationSavedVersions(left, right);
  assert.equal(diff.state, "ready"); assert.equal(diff.changed, true); assert.equal(diff.sharedBaseChanged, true);
  assert.match(diff.notice, /Shared bases differ/u); assert.ok(diff.rows.every(row => row.kind === "same"));
  right.release.commonSource = { state: "absent" }; right.release.applicationSource.publicationId = other;
  right.content.publication.publicationId = other;
  assert.equal(compareConfigurationSavedVersions(left, right).referencesChanged, true);
});

test("protected or foreign version content does not become an empty successful diff", () => {
  for (const state of ["redacted", "unavailable", "absent"]) {
    assert.equal(compareConfigurationSavedVersions(version(), { ...version(), content: { state, message: "Not JSON" } }).state, "unavailable");
  }
  for (const field of ["applicationId", "configurationId", "schemaVersion", "environment"]) {
    const foreign = version(); foreign.release[field] = "foreign";
    assert.equal(compareConfigurationSavedVersions(version(), foreign).state, "unavailable");
  }
  const unrelated = version(); unrelated.content.publication.publicationId = other;
  assert.equal(compareConfigurationSavedVersions(version(), unrelated).state, "unavailable");
  assert.throws(() => projectConfigurationVersion(unrelated), TypeError);
});

test("comparison baseline is only the fresh actual uniform deployed ID, with explicit no-deployment and targeted states", () => {
  const selected = version(); selected.release.releaseId = other;
  assert.equal(compareWithDeployedConfiguration(map(), selected, version()).state, "ready");
  assert.match(compareWithDeployedConfiguration({ ...map(), defaultReleaseId: undefined, generation: undefined }, selected, version()).message, /No version is deployed/u);
  assert.match(compareWithDeployedConfiguration({ ...map(), overrides: [{ cohortId: "finance", releaseId: other }] }, selected, version()).message, /targeted/u);
  assert.match(compareWithDeployedConfiguration({ ...map(), defaultReleaseId: other }, selected, version()).message, /changed/u);
});

test("one Deployed badge uses actual map identity, while notes are human labels rather than invented ordinals", () => {
  assert.equal(configurationVersionDeployment(map(), releaseId), "deployed"); assert.equal(configurationVersionDeployment(map(), other), "saved");
  assert.equal(configurationVersionDeployment(undefined, releaseId), "unavailable");
  assert.equal(configurationVersionDeployment({ ...map(), overrides: [{ cohortId: "finance", releaseId: other }] }, releaseId), "targeted");
  assert.equal(configurationVersionLabel(version().release), "Regional settings");
  assert.equal(configurationVersionLabel({ ...version().release, reason: "" }), "Saved 2026-11-26T12:34:56.1234567Z");
});

test("version projection retains only native immutable facts and exact JSON, never credentials or masked content", () => {
  const value = version(); value.secret = "hidden"; value.release.privateActor = "hidden"; value.content.publication.credentials = "hidden";
  const projected = projectConfigurationVersion(value);
  assert.equal(projected.content.publication.payloadJson, raw); assert.equal(projected.secret, undefined);
  assert.equal(projected.release.privateActor, undefined); assert.equal(projected.content.publication.credentials, undefined);
  assert.deepEqual(projectConfigurationVersion({ ...value, content: { state: "redacted", message: "Protected", publication: value.content.publication } }).content,
    { state: "redacted", message: "Protected" });
});
