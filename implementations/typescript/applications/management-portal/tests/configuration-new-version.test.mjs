import assert from "node:assert/strict";
import test from "node:test";
import { configurationNewVersionDraft } from "../src/configuration-new-version.ts";
import { reviewConfigurationCreation } from "../src/configuration-creation-model.ts";

const app = "11111111-1111-4111-8111-111111111111", source = "22222222-2222-4222-8222-222222222222";
const binding = "33333333-3333-4333-8333-333333333333", other = "44444444-4444-4444-8444-444444444444";
const raw = String.raw` {"Large":9007199254740993,"Exponent":1.2300e+40,"Unicode":"\u65e5 日本語 🌍","False":false,"Zero":-0,"Null":null} `;
const context = { applicationId: app, application: "Country Services", environment: "Development", schemaVersion: "1", hostApplicationName: "CountrySettings", eTag: '"creation.5"' };
const review = reviewConfigurationCreation(context, "test.andrew", raw, "  Exports for Europe  ", "createGeneric");
function result() {
  return { family: "management.configuration.create", key: "setup-original", target: { applicationId: app }, disposition: "applied",
    originalCompletion: "committed", resultSource: "prepared", transport: { kind: "http", status: 200, cleanupSucceeded: true, mutationUncertain: false },
    decision: { kind: "configurationCreated", decision: "applied", applicationId: app, configurationId: "test.andrew", schemaVersion: "1",
      sourceId: source, bindingId: binding, hostApplicationName: "CountrySettings" } };
}

test("one Save hands off exact unmodified JSON only after its own known setup and current session, without treating setup as publication", () => {
  const value = configurationNewVersionDraft(review, result(), "setup-original", "current-session", "current-session");
  assert.equal(value.raw, raw); assert.equal(value.configurationId, "test.andrew"); assert.equal(value.sourceId, source);
  assert.equal(value.bindingId, binding); assert.equal(value.environment, "Development"); assert.equal(value.sessionKey, "current-session");
  assert.ok(Object.isFrozen(value)); assert.equal("publicationId" in value, false); assert.equal("releaseId" in value, false);
  assert.equal("operationKey" in value, false); assert.equal(review.reason, "  Exports for Europe  ");
});

test("foreign original key, derived application/config/schema/host/source or session never admits the parent JSON", () => {
  const changes = [value => { value.key = "other-original"; }, value => { value.family = "management.configuration.release.create"; },
    value => { value.target.applicationId = other; }, value => { value.decision.applicationId = other; },
    value => { value.decision.configurationId = "other"; }, value => { value.decision.schemaVersion = "2"; },
    value => { value.decision.hostApplicationName = "OtherSettings"; }, value => { value.decision.sourceId = "not-a-native-id"; },
    value => { value.decision.bindingId = "not-a-native-id"; }, value => { value.decision.kind = "sourcePublished"; }];
  for (const change of changes) {
    const value = result(); change(value);
    assert.throws(() => configurationNewVersionDraft(review, value, "setup-original", "current-session", "current-session"), TypeError);
  }
  assert.throws(() => configurationNewVersionDraft(review, result(), "setup-original", "current-session", "new-session"), TypeError);
  assert.throws(() => configurationNewVersionDraft(review, result(), "setup-original", "current-session", undefined), TypeError);
});

test("unknown/rejected setup and failed current/native cleanup retain a partial Save instead of authorizing the next original", () => {
  for (const change of [value => { value.disposition = "unresolved"; }, value => { value.originalCompletion = "unknown"; },
    value => { value.disposition = "rejected"; }, value => { value.transport.status = 503; },
    value => { value.transport.cleanupSucceeded = false; }, value => { value.transport.mutationUncertain = true; },
    value => { value.attemptCompletion = { cleanupSucceeded: false }; },
    value => { value.recovery = { state: "retained", previousTransport: { ...value.transport, cleanupSucceeded: false }, currentDisposition: "unresolved" }; },
    value => { value.recovery = { state: "conflicting", previousTransport: value.transport, currentDisposition: "rejected" }; }]) {
    const value = result(); change(value);
    assert.throws(() => configurationNewVersionDraft(review, value, "setup-original", "current-session", "current-session"), TypeError);
  }
});
