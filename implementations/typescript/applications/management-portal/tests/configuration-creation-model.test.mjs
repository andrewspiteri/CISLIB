import assert from "node:assert/strict";
import test from "node:test";
import { initialConfigurationDraftMatches, projectConfigurationCreationContext, reviewConfigurationCreation } from "../src/configuration-creation-model.ts";

const app = "11111111-1111-4111-8111-111111111111", binding = "22222222-2222-4222-8222-222222222222", source = "33333333-3333-4333-8333-333333333333";
const context = { applicationId: app, application: "CountryData", environment: "staging", eTag: '"create"', schemaVersion: "1", hostApplicationName: "GenericConfiguration" };
const raw = '{ "Large":9007199254740993,"Ratio":1.2300e+4,"Text":"\\u65e5" }';

test("creation review uses fixed native schema and preserves exact JSON only in memory, separate from the command", () => {
  const review = reviewConfigurationCreation(context, "feature-flags", raw, "Initial flags", "createGeneric");
  assert.equal(review.raw, raw); assert.equal(review.ifMatch, context.eTag); assert.equal(review.schemaVersion, "1");
  assert.equal(review.configurationId, "feature-flags"); assert.ok(Object.isFrozen(review));
  assert.equal(reviewConfigurationCreation(context, "feature-flags", raw, "Reuse explicitly", "reuseGeneric").intent, "reuseGeneric");
  assert.throws(() => reviewConfigurationCreation({ ...context, schemaVersion: "2" }, "feature-flags", raw, "reason", "createGeneric"));
  for (const input of ["null", "[]", "false", "1", '{"a":1,"a":2}', '{"a":']) { assert.throws(() => reviewConfigurationCreation(context, "feature-flags", input, "reason", "createGeneric")); }
  assert.throws(() => reviewConfigurationCreation(context, "", raw, "reason", "createGeneric"));
  assert.throws(() => reviewConfigurationCreation(context, "feature-flags", raw, "", "createGeneric"));
});

test("creation context is a closed browser projection and cannot smuggle defaults, credentials or arbitrary native additions", () => {
  const value = projectConfigurationCreationContext({ ...context, defaultsJson: raw, accessToken: "secret", future: {} });
  assert.deepEqual(value, context); assert.ok(Object.isFrozen(value));
});

test("in-memory creation draft can enter editor only after exact actual source/binding/environment/session match", () => {
  const seed = { applicationId: app, environment: "staging", configurationId: "feature-flags", schemaVersion: "1", bindingId: binding,
    sourceId: source, hostApplicationName: "GenericConfiguration", sessionKey: "operator-session-service", raw };
  const actual = { context: { ...context, configurationId: "feature-flags", bindingId: binding, selectedSource: "application", applicationSource: { state: "available", sourceId: source } } };
  assert.equal(initialConfigurationDraftMatches(seed, actual, seed.sessionKey), true);
  for (const field of ["applicationId", "environment", "configurationId", "schemaVersion", "bindingId", "hostApplicationName"]) {
    assert.equal(initialConfigurationDraftMatches(seed, { context: { ...actual.context, [field]: "foreign" } }, seed.sessionKey), false, field);
  }
  assert.equal(initialConfigurationDraftMatches(seed, actual, "renewed-session"), false);
  for (const slot of [{ state: "redacted" }, { state: "absent" }, { state: "unavailable", sourceId: source }, { state: "available", sourceId: binding }]) {
    assert.equal(initialConfigurationDraftMatches(seed, { context: { ...actual.context, applicationSource: slot } }, seed.sessionKey), false);
  }
  assert.equal(initialConfigurationDraftMatches(seed, { context: { ...actual.context, selectedSource: "common" } }, seed.sessionKey), false);
});
