import assert from "node:assert/strict";
import test from "node:test";
import { captureConfig, captureStoreConfig } from "../src/server/config.ts";
import { allowedAuthAction, hasUniqueBoundedParameters, mapVerifiedOperator, safeReturnUrl, sameOriginMutation } from "../src/server/auth-policy.ts";

const tenant = "11111111-1111-1111-1111-111111111111";
const client = "22222222-2222-2222-2222-222222222222";
const subject = "33333333-3333-3333-3333-333333333333";
const environment = {
  NEXTAUTH_URL: "https://portal.example/", NEXTAUTH_SECRET: "fixture-owned-secret-" + "x".repeat(32),
  CIS_PORTAL_MANAGEMENT_URL: "https://management.example/platform/", CIS_PORTAL_TENANT_ID: tenant,
  CIS_PORTAL_CLIENT_ID: client, CIS_PORTAL_CLIENT_SECRET: "fixture-owned-client-secret",
  CIS_PORTAL_MANAGEMENT_AUDIENCE: "44444444-4444-4444-4444-444444444444",
  CIS_PORTAL_ALLOWED_SUBJECTS: subject,
  CIS_PORTAL_SESSION_DATABASE: "postgresql://fixture:fixture@127.0.0.1:5432/owned_fixture"
};
const config = captureConfig(environment);

test("configuration fixes trusted endpoints and scope without reading ambient fallback credentials", () => {
  assert.equal(config.managementScope, "api://44444444-4444-4444-4444-444444444444/management.operator");
  assert.equal(config.managementUrl, "https://management.example/platform/");
  assert.throws(() => captureConfig({ ...environment, NEXTAUTH_URL_INTERNAL: "http://attacker/" }));
  assert.throws(() => captureConfig({ ...environment, CIS_PORTAL_MANAGEMENT_URL: "https://user:secret@management.example/" }));
  assert.throws(() => captureConfig({ ...environment, NEXTAUTH_URL: "https://portal.example/other" }));
  assert.throws(() => captureConfig({ ...environment, CIS_PORTAL_ALLOWED_SUBJECTS: `${subject},${subject}` }));
  assert.throws(() => captureConfig({ ...environment, CIS_PORTAL_TENANT_ID: "00000000-0000-0000-0000-000000000000" }));
});

test("session store accepts only the owned namespace and validated remote TLS", () => {
  assert.equal(captureStoreConfig(environment).databaseSchema, "cis_portal");
  assert.equal(new URL(captureStoreConfig({ ...environment,
    CIS_PORTAL_SESSION_DATABASE: "postgresql://fixture:secret@127.0.0.1/owned_fixture" }).databaseUrl).port, "5432");
  assert.throws(() => captureStoreConfig({ ...environment, CIS_PORTAL_SESSION_SCHEMA: "public" }));
  assert.throws(() => captureStoreConfig({ ...environment, CIS_PORTAL_SESSION_DATABASE: "postgresql://fixture@db.example/portal" }));
  assert.equal(captureStoreConfig({ ...environment,
    CIS_PORTAL_SESSION_DATABASE: "postgresql://fixture:secret@db.example/portal?sslmode=verify-full" }).databaseSchema, "cis_portal");
  for (const database of ["postgresql://fixture@127.0.0.1/portal", "postgresql://fixture:secret@127.0.0.1/",
    "postgresql://fixture:secret@db.example/portal?sslmode=verify-full&sslmode=disable",
    "postgresql://fixture:secret@127.0.0.1/portal?options=untrusted"]) {
    assert.throws(() => captureStoreConfig({ ...environment, CIS_PORTAL_SESSION_DATABASE: database }));
  }
  assert.throws(() => captureStoreConfig({ ...environment, CIS_PORTAL_SESSION_DATABASE: "not a URI:private-password" }),
    error => error instanceof TypeError && !String(error).includes("private-password") && !("input" in error));
});

test("only the configured issuer, tenant and allowed object ID map after library verification", () => {
  const claims = { iss: `https://login.microsoftonline.com/${tenant}/v2.0`, tid: tenant, oid: subject, sub: "pairwise-only", name: "Operator" };
  const tokens = { access_token: "fixture-owned-bearer", expires_at: 1600 };
  const mapped = mapVerifiedOperator(claims, tokens, config, 1000);
  assert.equal(mapped.accountId, subject);
  assert.equal(mapped.email, `${subject}@${tenant}.invalid`);
  assert.throws(() => mapVerifiedOperator({ ...claims, iss: "https://other.example/" }, tokens, config, 1000));
  assert.throws(() => mapVerifiedOperator({ ...claims, tid: client }, tokens, config, 1000));
  assert.throws(() => mapVerifiedOperator({ ...claims, oid: client, email: mapped.email }, tokens, config, 1000));
  assert.throws(() => mapVerifiedOperator({ ...claims, oid: undefined, sub: subject }, tokens, config, 1000));
  // These tests are profile-policy checks, not JWT signature/nonce proof.
});

test("expired, malformed and unexpectedly long-lived credentials cannot create a session", () => {
  const claims = { iss: `https://login.microsoftonline.com/${tenant}/v2.0`, tid: tenant, oid: subject };
  for (const expires_at of [1000, 1029, NaN, "1600", 1000 + 86401]) {
    assert.throws(() => mapVerifiedOperator(claims, { access_token: "fixture-bearer", expires_at }, config, 1000));
  }
  assert.throws(() => mapVerifiedOperator(claims, { access_token: "line\nbreak", expires_at: 1600 }, config, 1000));
});

test("auth HTTP allowlist denies tokens, account administration, alternative providers and unsupported methods", () => {
  assert.equal(allowedAuthAction("GET", ["callback", "cis-management"]), true);
  assert.equal(allowedAuthAction("POST", ["signin", "cis-management"]), true);
  assert.equal(allowedAuthAction("POST", ["signout"]), true);
  for (const path of [["get-access-token"], ["refresh-token"], ["session"], ["account"], ["link-account"], ["callback", "other"]]) {
    assert.equal(allowedAuthAction("GET", path), false);
    assert.equal(allowedAuthAction("POST", path), false);
  }
  assert.equal(allowedAuthAction("DELETE", ["signin", "cis-management"]), false);
});

test("mutating auth requests require the fixed portal origin", () => {
  assert.equal(sameOriginMutation(new Headers({ origin: config.origin }), config.origin), true);
  assert.equal(sameOriginMutation(new Headers(), config.origin), false);
  assert.equal(sameOriginMutation(new Headers({ origin: "https://other.example" }), config.origin), false);
  assert.equal(sameOriginMutation(new Headers({ origin: config.origin, "sec-fetch-site": "cross-site" }), config.origin), false);
});

test("browser input cannot override scopes or duplicate callback and CSRF fields", () => {
  assert.equal(hasUniqueBoundedParameters(new URLSearchParams("csrfToken=owned&json=true"), ["csrfToken", "callbackUrl", "json"], 4096), true);
  for (const query of ["csrfToken=a&csrfToken=b", "scope=arbitrary", "client_id=other", "callbackUrl=%00"]) {
    assert.equal(hasUniqueBoundedParameters(new URLSearchParams(query), ["csrfToken", "callbackUrl", "json"], 4096), false);
  }
});

test("return navigation remains within the portal application paths", () => {
  assert.equal(safeReturnUrl("/applications?query=Country", config.origin), config.origin + "/applications?query=Country");
  for (const target of ["https://other.example/", "//other.example/", "/api/auth/signout", "/applications\\@other.example", "javascript:alert(1)"]) {
    assert.equal(safeReturnUrl(target, config.origin), config.origin + "/applications");
  }
});
