import assert from "node:assert/strict";
import test from "node:test";
import { assignedConfigurationLayer, configurationAction, configurationDetailMatches, configurationLayerAccess, configurationOrigin,
  projectConfigurationDetail, projectConfigurationRowActions, projectConfigurationSummaryPage } from "../src/configuration-list-model.ts";

const app = "11111111-1111-4111-8111-111111111111", binding = "22222222-2222-4222-8222-222222222222";
const source = "33333333-3333-4333-8333-333333333333", publication = "44444444-4444-4444-8444-444444444444";
const common = "55555555-5555-4555-8555-555555555555";
const raw = '{ "Large":9007199254740993,"Ratio":1.2300e+4,"Unicode":"日本語" }';
const absent = { state: "absent" }, present = { state: "present", publicationId: publication }, shared = { state: "present", publicationId: common };
function snapshot(content) { return { applicationId: app, application: "CountryData", environment: "staging", configurationId: "settings",
  schemaVersion: "9007199254740993", bindingId: binding, hostApplicationName: "CountrySettings", origin: "shared", hasCohortOverrides: true,
  generation: "0", content, actions: { state: "available", binding: { resourceId: binding, actions: [] }, application: { resourceId: source,
    actions: [{ operationId: "publishConfigurationSource", supported: true, allowed: false }] } } }; }

test("assigned layer chooses only the default release's exact raw layer and never joins JSON", () => {
  for (const [c, a, origin, layer, ref] of [[shared, absent, "shared", "common", common], [absent, present, "applicationOnly", "application", publication],
    [shared, present, "applicationOverride", "application", publication], [absent, absent, "defaultsOnly", "common", undefined]]) {
    const chosen = assignedConfigurationLayer({ commonSource: c, applicationSource: a });
    assert.equal(chosen.origin, origin); assert.equal(chosen.layer, layer); assert.equal(chosen.reference.publicationId, ref);
  }
  const explicit = assignedConfigurationLayer({ commonSource: shared, applicationSource: present }, "common");
  assert.equal(explicit.layer, "common"); assert.equal(explicit.reference.publicationId, common);
});

test("unreported and future summary origins remain neutral; real defaults-only and mixed differ from Shared", () => {
  for (const origin of [undefined, "futureLayer", "", "Shared"]) { assert.deepEqual(configurationOrigin(origin), { label: "Not reported", status: "Not reported" }); }
  assert.equal(configurationOrigin("defaultsOnly").label, "Declared defaults"); assert.equal(configurationOrigin("mixed").label, "Varies by cohort");
  assert.equal(configurationOrigin("protected").status, "Not available"); assert.equal(configurationOrigin("unassigned").status, "Unassigned");
});

test("summary allowlist retains real source facts and exact schema while discarding all future content and principals", () => {
  const result = projectConfigurationSummaryPage({ items: [{ ...snapshot({ state: "absent", message: "No publication." }), sourceId: source,
    rawJson: raw, defaultsJson: raw, principal: "secret", shapeKind: "object", origin: "shared", modifiedAtUtc: "2026-11-26T12:34:56.1234567Z" }], nextCursor: "cursor" });
  assert.equal(result.items[0].schemaVersion, "9007199254740993"); assert.equal(result.items[0].origin, "shared");
  assert.equal(result.items[0].rawJson, undefined); assert.equal(result.items[0].defaultsJson, undefined); assert.equal(result.items[0].principal, undefined);
  assert.equal(result.items[0].content, undefined); assert.ok(Object.isFrozen(result.items[0]));
  assert.equal(result.items[0].modifiedAtUtc, "2026-11-26T12:34:56.1234567Z", "Preserve the precise native assigned-layer time, not browser now or history order.");
  assert.equal(projectConfigurationSummaryPage({ items: [{ ...result.items[0], modifiedAtUtc: undefined }] }).items[0].modifiedAtUtc, undefined);
});

test("JSON detail projection preserves exact payload without leaking defaults/future fields; absence/protection never becomes JSON", () => {
  const value = snapshot({ state: "present", layer: "application", publication: { publicationId: publication, sourceId: source, configurationId: "settings", schemaVersion: "9007199254740993",
    environment: "staging", kind: "application", applicationId: app, sha256: "a".repeat(64), actor: "actor", createdAtUtc: "2026-11-26T12:00:00.0000001Z", reason: "reviewed", payloadJson: raw, secret: "hidden" } });
  value.defaultsJson = raw; value.futurePrincipal = "secret"; value.actions.application.secret = "secret";
  const result = projectConfigurationDetail(value);
  assert.equal(result.content.publication.payloadJson, raw); assert.equal(result.content.publication.secret, undefined);
  assert.equal(result.content.publication.actor, undefined); assert.equal(result.content.publication.reason, undefined);
  assert.equal(result.content.publication.createdAtUtc, "2026-11-26T12:00:00.0000001Z");
  assert.equal(result.defaultsJson, undefined); assert.equal(result.futurePrincipal, undefined); assert.equal(result.actions.application.secret, undefined);
  assert.equal(result.generation, "0"); assert.equal(configurationAction(result, "application", "publishConfigurationSource"), false);
  assert.ok(configurationDetailMatches(result, app, "settings", "9007199254740993")); assert.equal(configurationDetailMatches(result, app, "settings", "1"), false);
  for (const state of ["absent", "unassigned", "redacted", "unavailable"]) {
    const projected = projectConfigurationDetail(snapshot({ state, message: "Not JSON.", publication: value.content.publication }));
    assert.deepEqual(projected.content, { state, message: "Not JSON." });
  }
});

test("row action metadata carries only native source/binding hints, never JSON or a guessed permission", () => {
  const value = snapshot({ state: "present", layer: "application", publication: { payloadJson: raw } });
  const result = projectConfigurationRowActions({ ...value, defaultsJson: raw, credentials: "secret" });
  assert.equal(result.content, undefined); assert.equal(result.defaultsJson, undefined); assert.equal(result.credentials, undefined);
  assert.equal(configurationAction(result, "application", "publishConfigurationSource"), false);
  assert.equal(configurationAction(result, "binding", "replaceConfigurationAssignments"), false);
  assert.ok(Object.isFrozen(result.actions.application.actions));
});

test("assigned JSON protection follows the exact native selected source, independently for shared and Application layers", () => {
  const context = { selectedSource: "application", applicationSource: { state: "available", sourceId: source },
    commonSource: { state: "available", sourceId: common }, defaults: { state: "redacted" } };
  assert.equal(configurationLayerAccess(context, "application"), "redacted");
  assert.equal(configurationLayerAccess(context, "common"), undefined, "Application denial says nothing about shared ContentRead.");
  assert.equal(configurationLayerAccess({ ...context, defaults: { state: "available", defaultsJson: "{}" } }, "common"), undefined,
    "Application access does not grant shared ContentRead.");
  const commonContext = { ...context, selectedSource: "common", defaults: { state: "available", defaultsJson: "{}" } };
  assert.equal(configurationLayerAccess(commonContext, "common"), "available");
  assert.equal(configurationLayerAccess(commonContext, "application"), undefined, "Shared access does not grant Application ContentRead.");
  assert.equal(configurationLayerAccess({ ...commonContext, defaults: { state: "redacted" } }, "common"), "redacted");
  assert.equal(configurationLayerAccess({ ...context, defaults: { state: "unavailable" } }, "application"), "unavailable");
  assert.equal(configurationLayerAccess({ ...commonContext, commonSource: { state: "absent" } }, "common"), "unavailable");
});
