import assert from "node:assert/strict";
import test from "node:test";
import { createPortalSessionContext } from "../src/server/session-context.ts";
import { capturePortalContext } from "../src/portal-context.ts";

const config = Object.freeze({ authSecret: "fixture-owned-secret-" + "x".repeat(32),
  managementUrl: "https://management.example/platform/", managementScope: "api://11111111-1111-1111-1111-111111111111/management.operator" });
const user = "22222222-2222-2222-2222-222222222222";
const otherUser = "33333333-3333-3333-3333-333333333333";
const ticket = "fixture-private-session-ticket";

test("validated input produces a frozen nonsecret context, without raw database identity or ticket", () => {
  const context = createPortalSessionContext(config, user, ticket, 2000, 1000);
  assert.ok(context);
  assert.equal(Object.isFrozen(context), true);
  assert.deepEqual(Object.keys(context).sort(), ["expiresAtMs", "operatorId", "serviceAssociation", "sessionAssociation"]);
  for (const key of ["operatorId", "sessionAssociation", "serviceAssociation"]) { assert.match(context[key], /^[a-f0-9]{64}$/u); }
  assert.equal(new Set([context.operatorId, context.sessionAssociation, context.serviceAssociation]).size, 3);
  assert.deepEqual(capturePortalContext(context), context);
  assert.deepEqual(createPortalSessionContext(config, user, ticket, 2000, 1001), context);
  const serialized = JSON.stringify(context);
  for (const privateValue of [config.authSecret, user, ticket, config.managementUrl, config.managementScope]) {
    assert.equal(serialized.includes(privateValue), false);
  }
});

test("same-user reauthentication changes only session association while another operator stays separate", () => {
  const first = createPortalSessionContext(config, user, ticket, 2000, 1000);
  const next = createPortalSessionContext(config, user, "another-private-session-ticket", 2000, 1000);
  const other = createPortalSessionContext(config, otherUser, ticket, 2000, 1000);
  assert.ok(first && next && other);
  assert.equal(next.operatorId, first.operatorId);
  assert.equal(next.serviceAssociation, first.serviceAssociation);
  assert.notEqual(next.sessionAssociation, first.sessionAssociation);
  assert.notEqual(other.operatorId, first.operatorId);
  assert.notEqual(other.sessionAssociation, first.sessionAssociation);
  assert.equal(other.serviceAssociation, first.serviceAssociation);
});

test("service association follows fixed canonical endpoint and scope, and is not a backend authority assertion", () => {
  const first = createPortalSessionContext(config, user, ticket, 2000, 1000);
  const equivalent = createPortalSessionContext({ ...config, managementUrl: "https://MANAGEMENT.example/platform" }, user, ticket, 2000, 1000);
  const endpoint = createPortalSessionContext({ ...config, managementUrl: "https://management.example/other/" }, user, ticket, 2000, 1000);
  const scope = createPortalSessionContext({ ...config, managementScope: "api://11111111-1111-1111-1111-111111111111/another.scope" }, user, ticket, 2000, 1000);
  assert.ok(first && equivalent && endpoint && scope);
  assert.deepEqual(equivalent, first);
  for (const changed of [endpoint, scope]) {
    assert.notEqual(changed.serviceAssociation, first.serviceAssociation);
    assert.equal(changed.operatorId, first.operatorId);
    assert.equal(changed.sessionAssociation, first.sessionAssociation);
  }
  assert.equal("authorityId" in first, false);
  assert.equal("recoveryEpoch" in first, false);
});

test("replica-stable configured keys and conservative secret rotation remain distinct from session lifetime", () => {
  const first = createPortalSessionContext(config, user, ticket, 2000, 1000);
  const replica = createPortalSessionContext({ ...config }, user, ticket, 2000, 1000);
  const shorter = createPortalSessionContext(config, user, ticket, 1500, 1000);
  const rotated = createPortalSessionContext({ ...config, authSecret: "another-fixture-secret-" + "y".repeat(32) }, user, ticket, 2000, 1000);
  assert.ok(first && replica && shorter && rotated);
  assert.deepEqual(replica, first);
  for (const key of ["operatorId", "sessionAssociation", "serviceAssociation"]) {
    assert.equal(shorter[key], first[key]);
    assert.notEqual(rotated[key], first[key]);
  }
  assert.equal(shorter.expiresAtMs, 1500);
});

test("malformed or expired association inputs refuse without producing a partial context", () => {
  for (const invalidUser of ["", user.toUpperCase().replace("2", "A"), "00000000-0000-0000-0000-000000000000", "operator@example.test", "x".repeat(4096)]) {
    assert.equal(createPortalSessionContext(config, invalidUser, ticket, 2000, 1000), undefined);
  }
  for (const invalidTicket of ["", "has space", "line\nbreak", "\u0000", "non-ascii-é", "x".repeat(4097)]) {
    assert.equal(createPortalSessionContext(config, user, invalidTicket, 2000, 1000), undefined);
  }
  for (const expiresAtMs of [0, 999, 1000, NaN, Infinity, 1500.5, 8640000000000001]) {
    assert.equal(createPortalSessionContext(config, user, ticket, expiresAtMs, 1000), undefined);
  }
  for (const nowMs of [-1, NaN, Infinity, 1000.5]) {
    assert.equal(createPortalSessionContext(config, user, ticket, 2000, nowMs), undefined);
  }
  for (const altered of [{ authSecret: "short" }, { managementScope: "scope with spaces" }, { managementScope: "x".repeat(513) },
    { managementUrl: "http://management.example/" }, { managementUrl: "https://user:private@management.example/" },
    { managementUrl: "https://management.example/?scope=other" }, { managementUrl: "https://management.example/%2fother/" }]) {
    assert.equal(createPortalSessionContext({ ...config, ...altered }, user, ticket, 2000, 1000), undefined);
  }
});
