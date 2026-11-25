// Named RM09 integration fixture: real Auth.js, PostgreSQL sessions and native Management.
// Its small signed issuer exists only in tests; production contains no issuer/mock mode.
import assert from "node:assert/strict";
import { createHash, createPrivateKey, createPublicKey, randomBytes, randomUUID, sign, constants } from "node:crypto";
import { readFile } from "node:fs/promises";
import { mkdirSync, writeFileSync } from "node:fs";
import { fork } from "node:child_process";
import https from "node:https";
import net from "node:net";
import { join, dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { request as playwrightRequest } from "@playwright/test";
import { verifySessionStoreDrain } from "./native-session-store-consumer.mjs";
import { captureStoreConfig } from "../src/server/config.ts";
import { createSessionStore } from "../src/server/store.ts";

const directory = process.argv[2];
assert.ok(directory, "The existing native fixture supplies a newly owned private directory.");
const input = JSON.parse(await readFile(join(directory, "portal-fixture.json"), "utf8"));
const portalDirectory = dirname(dirname(fileURLToPath(import.meta.url)));
const evidenceDirectory = join(resolve(portalDirectory, "../../../.."), ".artifacts", "management-portal", "CIS-0029", "WORK-004", "native-host-" + randomUUID());
mkdirSync(evidenceDirectory, { recursive: true });
const signing = createPrivateKey(await readFile(join(directory, "oidc.key")));
const jwk = { ...createPublicKey(signing).export({ format: "jwk" }), kid: "rm09-portal-owned-key", alg: "RS256", use: "sig" };
const issuer = `${input.authority}/${input.tenant}/v2.0`;
const codes = new Map();
const issuedTokens = [];
const issuerSockets = new Set();
let phase = "preparation";
let host;
let hostExit;
let hostOutput = "";
let hostErrors = "";
let hostStopped = false;
let hostReady = false;
let hostClosed = false;
let hostExited = false;
let hostStdoutClosed = false;
let hostStderrClosed = false;
let hostDisconnected = false;
let hostNativeJoined = false;
let firstFailurePhase;
const contexts = [];
const cases = [];
const failures = [];
const phaseHistory = [];
const observations = {};
recordPhase("preparation");
const ownerStore = createSessionStore(captureStoreConfig({ CIS_PORTAL_SESSION_DATABASE: input.sessionOwnerUrl,
  CIS_PORTAL_SESSION_SCHEMA: input.sessionSchema }));
const owner = ownerStore.pool;
const authority = https.createServer({ cert: await readFile(join(directory, "server.pem")),
  key: await readFile(join(directory, "server.key")), minVersion: "TLSv1.2" });
authority.on("connection", socket => { issuerSockets.add(socket); socket.once("close", () => issuerSockets.delete(socket)); });
authority.on("request", (request, response) => {
  void issue(request, response).catch(() => { if (!response.headersSent) { response.writeHead(500); } response.end(); });
});

try {
  cases.push(...await verifySessionStoreDrain({ owner, databaseUrl: input.sessionRuntimeUrl, schema: input.sessionSchema,
    reportPhase: recordPhase }));
  recordPhase("issuer-listen");
  await new Promise((resolve, reject) => { authority.once("error", reject); authority.listen(input.issuerPort, "127.0.0.1", resolve); });
  const port = await unusedPort();
  const origin = `https://127.0.0.1:${port}`;
  const environment = { ...process.env,
    NEXTAUTH_URL: origin, NEXTAUTH_SECRET: input.authSecret,
    CIS_PORTAL_MANAGEMENT_URL: input.managementUrl, CIS_PORTAL_MANAGEMENT_CA_FILE: join(directory, "management-root.pem"),
    CIS_PORTAL_TENANT_ID: input.tenant, CIS_PORTAL_CLIENT_ID: input.portalClient,
    CIS_PORTAL_CLIENT_SECRET: input.clientSecret, CIS_PORTAL_MANAGEMENT_AUDIENCE: input.audience,
    CIS_PORTAL_OPERATOR_SCOPE: "management.operator", CIS_PORTAL_ALLOWED_SUBJECTS: `${input.operatorA},${input.operatorB}`,
    CIS_PORTAL_AUTHORITY_ORIGIN: input.authority, CIS_PORTAL_SESSION_DATABASE: input.sessionRuntimeUrl,
    CIS_PORTAL_SESSION_SCHEMA: input.sessionSchema, CIS_PORTAL_BIND_ADDRESS: "127.0.0.1", CIS_PORTAL_PORT: String(port),
    CIS_PORTAL_SERVER_CERTIFICATE_FILE: join(directory, "server.pem"), CIS_PORTAL_SERVER_KEY_FILE: join(directory, "server.key") };
  delete environment.NEXTAUTH_URL_INTERNAL;
  host = fork(join(portalDirectory, "server.mts"), [], { cwd: portalDirectory, env: environment,
    stdio: ["ignore", "pipe", "pipe", "ipc"], windowsHide: true });
  host.stdout.on("data", value => { hostOutput = (hostOutput + value.toString()).slice(-65536); });
  host.stderr.on("data", value => { hostErrors = (hostErrors + value.toString()).slice(-65536); });
  // Node 24's aggregate child close count can retain an explicitly disconnected
  // IPC contribution. Join every actual public resource event independently.
  host.once("close", () => { hostClosed = true; writePhase(); });
  const processExitObserved = new Promise(resolve => {
    host.once("exit", (code, signal) => { hostExited = true; writePhase(); resolve({ code, signal }); });
    host.once("error", error => resolve({ code: -1, error }));
  });
  const stdoutClosed = new Promise(resolve => {
    host.stdout.once("close", () => { hostStdoutClosed = true; writePhase(); resolve(); });
  });
  const stderrClosed = new Promise(resolve => {
    host.stderr.once("close", () => { hostStderrClosed = true; writePhase(); resolve(); });
  });
  const disconnected = new Promise(resolve => {
    host.once("disconnect", () => { hostDisconnected = true; writePhase(); resolve(); });
  });
  hostExit = Promise.all([processExitObserved, stdoutClosed, stderrClosed, disconnected]).then(([result]) => {
    hostNativeJoined = true;
    writePhase();
    return result;
  });
  recordPhase("host-api-contexts");
  const anonymous = await newContext(origin);
  const idp = await playwrightRequest.newContext({ ignoreHTTPSErrors: false, timeout: 15000 });
  contexts.push(idp);
  recordPhase("host-startup");
  const readyUntil = Date.now() + 30000;
  let ready = false;
  while (Date.now() < readyUntil && host.exitCode === null) {
    try {
      const response = await anonymous.get("/healthz", { timeout: 1000 });
      if (response.status() === 200) { ready = true; break; }
    } catch { /* Startup connection refusal is expected until the owned listener opens. */ }
    await new Promise(resolve => setTimeout(resolve, 100));
  }
  assert.ok(ready, "The ordinary production portal host must become ready.");
  hostReady = true;
  recordPhase("host-ready");
  const unauthenticated = await anonymous.get("/api/portal/applications");
  assert.equal(unauthenticated.status(), 401);
  assert.match(unauthenticated.headers()["cache-control"], /no-store/u);
  cases.push("unauthenticated-no-store");

  recordPhase("valid-operator-a");
  const a = await newContext(origin);
  await login(a, idp, origin, input.operatorA);
  await assertApplications(a, [input.applicationA]);
  const instanceResponse = await a.get(`/api/portal/applications/${input.applicationA}/instances`);
  assert.equal(instanceResponse.status(), 200);
  const instances = await instanceResponse.json();
  assert.equal(instances.state, "ready");
  assert.equal(instances.value.items.find(item => item.registrationId === input.registrationA)?.applicationVersion, "1.0.0");
  assert.equal(instances.value.items.find(item => item.registrationId === input.secondRegistration)?.applicationVersion, "2.4.2");
  const inventoryResponse = await a.get(`/api/portal/applications/${input.applicationA}/inventory`);
  assert.equal(inventoryResponse.status(), 200);
  const inventory = await inventoryResponse.json();
  assert.equal(inventory.state, "ready");
  assert.equal(inventory.value.items.length, 1);
  assert.equal(inventory.value.items[0].registrationId, input.registrationA);
  assert.equal(inventory.value.items[0].applicationVersion, "1.0.0");
  assert.equal(inventory.value.items[0].inventory.jobs[0].jobId, "customer.dormancy");
  const definitionResponse = await a.get(`/api/portal/applications/${input.applicationA}/configuration`);
  assert.equal(definitionResponse.status(), 200);
  const definitionText = await definitionResponse.text();
  assert.ok(!definitionText.includes("PORTAL_PRIVATE_DEFAULT") && !definitionText.includes("defaultsJson"));
  const definitions = JSON.parse(definitionText);
  assert.equal(definitions.state, "ready");
  assert.equal(definitions.value.items.length, 1);
  assert.equal(definitions.value.items[0].configurationId, "shared.settings");
  assert.equal(definitions.value.items[0].hostApplicationName, "FirstHost");
  cases.push("per-registration-version-and-exact-inventory", "genuine-metadata-only-config-definition");
  const cookieA = (await a.storageState()).cookies.find(cookie => cookie.name === "__Host-cis-portal.session-token");
  assert.ok(cookieA?.secure && cookieA.httpOnly && !cookieA.value.includes("."), "Only an opaque secure HttpOnly session ticket reaches the browser.");
  const denied = await a.get(`/api/portal/applications/${input.applicationB}`);
  assert.ok([403, 404].includes(denied.status()), "The native target grant must hide or deny the other application.");
  cases.push("real-code-pkce-id-token-native-grant", "opaque-session-cookie", "native-target-denial");

  recordPhase("valid-operator-b");
  const b = await newContext(origin);
  await login(b, idp, origin, input.operatorB);
  await assertApplications(b, [input.applicationB]);
  await assertApplications(a, [input.applicationA]);
  cases.push("independent-user-session-isolation");
  for (const context of [a, b]) {
    for (const path of ["session", "get-access-token", "refresh-token", "account", "link-account"]) {
      assert.equal((await context.get("/api/auth/" + path)).status(), 404);
    }
  }
  cases.push("browser-token-and-account-routes-denied");
  const retained = await owner.query("SELECT refresh_token, id_token FROM accounts");
  assert.equal(retained.rowCount, 2);
  assert.ok(retained.rows.every(row => row.refresh_token === null && row.id_token === null));
  cases.push("refresh-and-id-tokens-not-retained");

  recordPhase("no-account-linking");
  const activeCsrf = (await (await a.get("/api/auth/csrf")).json()).csrfToken;
  const attemptedLink = await a.post("/api/auth/signin/cis-management", {
    form: { csrfToken: activeCsrf, callbackUrl: origin + "/applications", json: "true" }, headers: { Origin: origin }, maxRedirects: 0 });
  assert.equal(attemptedLink.status(), 409);
  assert.equal((await a.get("/api/auth/callback/cis-management?code=untrusted&state=untrusted", { maxRedirects: 0 })).status(), 409);
  await assertApplications(a, [input.applicationA]);
  cases.push("active-session-cannot-link-another-account");

  recordPhase("closed-auth-inputs");
  assert.equal((await anonymous.post("/api/auth/signin/cis-management", {
    form: { csrfToken: "untrusted", scope: "arbitrary" }, headers: { Origin: origin } })).status(), 400);
  assert.equal((await a.post("/api/auth/signout", {
    form: { csrfToken: activeCsrf }, headers: { Origin: "https://other.example" } })).status(), 403);
  await assertApplications(a, [input.applicationA]);
  cases.push("scope-override-and-cross-origin-signout-denied");

  if (input.browser) {
    recordPhase("desktop-browser");
    const { verifyBrowser } = await import("./native-browser-checks.mjs");
    await verifyBrowser({ origin, applicationA: input.applicationA, applicationB: input.applicationB,
      registrationA: input.registrationA, operatorAContext: a, operatorBContext: b, directory });
  }

  recordPhase("expired-session-observe-clocks");
  const nodeNow = Date.now();
  const databaseClock = await owner.query("SELECT extract(epoch FROM clock_timestamp())*1000 AS milliseconds");
  observations.expiryDatabaseMinusNodeMilliseconds = Math.round(Number(databaseClock.rows[0].milliseconds) - nodeNow);
  recordPhase("expired-session-update-row");
  const expired = await owner.query('UPDATE sessions SET expires=$3 WHERE "userId" IN (SELECT "userId" FROM accounts WHERE provider=$1 AND "providerAccountId"=$2) RETURNING expires',
    ["cis-management", input.operatorA, new Date(Date.now() - 60000)]);
  observations.expiryUpdatedRows = expired.rowCount;
  const expiredDeadline = expired.rows[0]?.expires instanceof Date ? expired.rows[0].expires.getTime() : NaN;
  observations.expiryReturnedDeadlineMilliseconds = Number.isFinite(expiredDeadline) ? expiredDeadline : null;
  writePhase();
  assert.equal(expired.rowCount, 1, "The fixture must expire exactly the intended existing session row.");
  assert.ok(Number.isFinite(expiredDeadline) && expiredDeadline < Date.now(), "The persisted session deadline must be past on the validator's Node clock.");
  recordPhase("expired-session-protected-read");
  const expiredRead = await a.get("/api/portal/applications");
  observations.expiredSessionHttpStatus = expiredRead.status();
  writePhase();
  assert.equal(expiredRead.status(), 401, "The protected read must reject the expired persisted session.");
  recordPhase("expired-session-and-reauthentication");
  const before = issuedTokens.length;
  await login(a, idp, origin, input.operatorA);
  await assertApplications(a, [input.applicationA]);
  assert.ok(issuedTokens.length > before);
  const actual = await owner.query('SELECT access_token FROM accounts WHERE provider=$1 AND "providerAccountId"=$2', ["cis-management", input.operatorA]);
  assert.ok(actual.rows[0].access_token === issuedTokens.at(-1).access, "Validated reauthentication updates only that account's server-side credential.");
  cases.push("real-postgres-session-expiry", "reauthentication-updates-same-account");

  recordPhase("revocation-and-signout");
  await owner.query('DELETE FROM sessions WHERE "userId" IN (SELECT "userId" FROM accounts WHERE provider=$1 AND "providerAccountId"=$2)', ["cis-management", input.operatorA]);
  assert.equal((await a.get("/api/portal/applications")).status(), 401);
  await assertApplications(b, [input.applicationB]);
  const signoutCsrf = (await (await b.get("/api/auth/csrf")).json()).csrfToken;
  assert.equal((await b.post("/api/auth/signout", { form: { csrfToken: signoutCsrf, callbackUrl: origin + "/applications", json: "true" },
    headers: { Origin: origin }, maxRedirects: 0 })).status(), 200);
  assert.equal((await b.get("/api/portal/applications")).status(), 401);
  cases.push("real-postgres-session-revocation", "csrf-protected-signout");

  for (const failure of ["state", "nonce", "issuer", "audience", "signature"]) {
    recordPhase("invalid-" + failure);
    const context = await newContext(origin);
    await login(context, idp, origin, input.operatorA, failure);
    assert.equal((await context.get("/api/portal/applications")).status(), 401);
    cases.push("verified-callback-rejects-" + failure);
  }
  recordPhase("session-store-role-and-token-boundary");
  const probeStore = createSessionStore(captureStoreConfig({ CIS_PORTAL_SESSION_DATABASE: input.sessionRuntimeUrl,
    CIS_PORTAL_SESSION_SCHEMA: input.sessionSchema }));
  const probe = probeStore.pool;
  try {
    const permissions = await probe.query("SELECT rolsuper, rolcreatedb, rolcreaterole FROM pg_roles WHERE rolname=current_user");
    assert.ok(permissions.rows.every(row => !row.rolsuper && !row.rolcreatedb && !row.rolcreaterole));
  } finally { await probeStore.dispose(); }
  for (const issued of issuedTokens) {
    assert.ok(!hostOutput.includes(issued.access) && !hostErrors.includes(issued.access));
    assert.ok(!hostOutput.includes(issued.id) && !hostErrors.includes(issued.id));
  }
  cases.push("restricted-runtime-database-role", "no-token-logging");
  recordPhase("graceful-host-stop");
  host.disconnect();
  const exited = await hostExit;
  assert.equal(exited.code, 0, "The host must finish actual cleanup after parent disconnect.");
  hostStopped = true;
  cases.push("awaited-host-owner-stop");
} catch (error) {
  rememberFailure(error);
} finally {
  recordPhase("cleanup-api-contexts");
  const contextClosures = await Promise.allSettled(contexts.map(context => context.dispose()));
  for (const closure of contextClosures) { if (closure.status === "rejected") { rememberFailure(closure.reason); } }
  if (host !== undefined && !hostStopped) {
    recordPhase("cleanup-host-disconnect");
    try { if (host.connected) { host.disconnect(); } } catch (error) { rememberFailure(error); }
    try {
      recordPhase("cleanup-host-native-join");
      const exited = await hostExit;
      if (exited?.code !== 0) { throw new Error("The owned portal process failed during cleanup."); }
      hostStopped = true;
    } catch (error) { rememberFailure(error); }
  }
  recordPhase("cleanup-owner-session-store");
  try { await ownerStore.dispose(); } catch (error) { rememberFailure(error); }
  try {
    recordPhase("cleanup-test-issuer-native-join");
    const closed = authority.listening ? new Promise((resolve, reject) => authority.close(error => error ? reject(error) : resolve())) : Promise.resolve();
    const socketCloses = [...issuerSockets].map(socket => new Promise(resolve => socket.once("close", resolve)));
    authority.closeAllConnections();
    const closures = await Promise.allSettled([closed, ...socketCloses]);
    for (const closure of closures) { if (closure.status === "rejected") { rememberFailure(closure.reason); } }
    assert.equal(issuerSockets.size, 0);
  } catch (error) { rememberFailure(error); }
}
if (failures.length !== 0) {
  recordPhase("failed");
  writeFileSync(join(evidenceDirectory, "report.json"), JSON.stringify({ completed: false, cases, firstFailurePhase,
    failureCount: failures.length, observations, hostReady, hostStopped, hostExited, hostStdoutClosed, hostStderrClosed,
    hostDisconnected, hostNativeJoined, hostClosed, issuerSocketsDrained: issuerSockets.size === 0 }, null, 2));
  const failure = new AggregateError(failures, "Native portal execution or owned cleanup failed.");
  // Retain original and cleanup errors together; print only bounded credential-free diagnostics.
  const first = failure.errors[0];
  const message = first instanceof Error && first.name === "AssertionError" ? first.message.slice(0, 512) : "Native fixture operation failed.";
  process.stderr.write(`[PortalFixture] phase=${phase}; failures=${failure.errors.length}; ${message}\n`);
  process.stderr.write(hostErrors.slice(-8192));
  process.exitCode = 1;
} else {
  recordPhase("complete");
  const report = { cases, nativeOidc: true, sessionsRealPostgres: true, tokensStayedServerSide: true,
    observations, hostStopped, hostExited, hostStdoutClosed, hostStderrClosed, hostDisconnected, hostNativeJoined,
    hostClosed, socketsDrained: true, browserChecked: input.browser };
  writeFileSync(join(evidenceDirectory, "report.json"), JSON.stringify(report, null, 2));
  process.stdout.write(JSON.stringify(report));
}

function recordPhase(value) { phase = value; phaseHistory.push(value); if (phaseHistory.length > 32) { phaseHistory.shift(); } writePhase(); }
function writePhase() {
  // Only fixed fixture phases and case names are persisted; never native input or raw errors/logs.
  writeFileSync(join(evidenceDirectory, "phase.json"), JSON.stringify({ phase, phaseHistory, firstFailurePhase, cases, observations,
    failureCount: failures.length, hostSpawned: host !== undefined, hostReady, hostExited, hostStdoutClosed, hostStderrClosed,
    hostDisconnected, hostNativeJoined, hostClosed, hostStopped }, null, 2));
}
function rememberFailure(error) { firstFailurePhase ??= phase; failures.push(error); writePhase(); }

async function newContext(origin) {
  const context = await playwrightRequest.newContext({ baseURL: origin, ignoreHTTPSErrors: false, timeout: 15000 });
  contexts.push(context);
  return context;
}
async function assertApplications(context, expected) {
  const response = await context.get("/api/portal/applications");
  assert.equal(response.status(), 200, "An authenticated operator must reach the actual native Management read.");
  const text = await response.text();
  for (const issued of issuedTokens) { assert.ok(!text.includes(issued.access) && !text.includes(issued.id), "Backend tokens must remain server-side."); }
  const body = JSON.parse(text);
  assert.equal(body.state, "ready");
  assert.deepEqual(body.value.items.map(item => item.applicationId).sort(), [...expected].sort());
  assert.match(response.headers()["cache-control"], /no-store/u);
}
async function login(context, idp, origin, subject, failure) {
  const loginPhase = phase;
  recordPhase(loginPhase + "-csrf");
  const csrf = await context.get("/api/auth/csrf");
  assert.equal(csrf.status(), 200);
  const csrfToken = (await csrf.json()).csrfToken;
  assert.ok(typeof csrfToken === "string" && csrfToken.length > 0, "The maintained CSRF flow must return its token.");
  recordPhase(loginPhase + "-signin");
  const signin = await context.post("/api/auth/signin/cis-management", {
    form: { csrfToken, callbackUrl: origin + "/applications", json: "true" }, headers: { Origin: origin }, maxRedirects: 0 });
  assert.equal(signin.status(), 200, "The maintained sign-in flow must admit the configured provider.");
  const authorize = new URL((await signin.json()).url);
  assert.equal(authorize.origin, input.authority);
  assert.equal(authorize.searchParams.get("client_id"), input.portalClient);
  assert.equal(authorize.searchParams.get("code_challenge_method"), "S256");
  assert.equal(authorize.searchParams.get("response_type"), "code");
  assert.ok(authorize.searchParams.get("nonce") && authorize.searchParams.get("state"));
  assert.ok(!authorize.searchParams.get("scope").includes("offline_access"));
  authorize.searchParams.set("fixture_subject", subject);
  if (failure !== undefined) { authorize.searchParams.set("fixture_failure", failure); }
  recordPhase(loginPhase + "-authorize");
  const issued = await idp.get(authorize.href, { maxRedirects: 0 });
  assert.equal(issued.status(), 302);
  const callback = new URL(issued.headers().location);
  if (failure === "state") { callback.searchParams.set("state", "wrong-state"); }
  recordPhase(loginPhase + "-callback");
  const result = await context.get(callback.href, { maxRedirects: 0 });
  assert.equal(result.status(), 302);
  assert.match(result.headers()["cache-control"], /no-store/u);
  if (failure === undefined) { assert.equal(new URL(result.headers().location).pathname, "/applications"); }
  recordPhase(loginPhase + "-complete");
}
async function issue(request, response) {
  response.setHeader("Cache-Control", "no-store");
  const url = new URL(request.url, input.authority);
  const prefix = "/" + input.tenant;
  if (request.method === "GET" && url.pathname === prefix + "/discovery/v2.0/keys") {
    response.writeHead(200, { "Content-Type": "application/json" });
    response.end(JSON.stringify({ keys: [jwk] }));
    return;
  }
  if (request.method === "GET" && url.pathname === prefix + "/oauth2/v2.0/authorize") {
    assert.equal(url.searchParams.get("client_id"), input.portalClient);
    const redirect = new URL(url.searchParams.get("redirect_uri"));
    assert.equal(redirect.hostname, "127.0.0.1");
    assert.equal(redirect.pathname, "/api/auth/callback/cis-management");
    assert.equal(url.searchParams.get("code_challenge_method"), "S256");
    const subject = url.searchParams.get("fixture_subject");
    assert.ok([input.operatorA, input.operatorB].includes(subject));
    assert.ok(codes.size < 32);
    const code = randomBytes(24).toString("base64url");
    codes.set(code, { subject, redirect: redirect.href, nonce: url.searchParams.get("nonce"),
      state: url.searchParams.get("state"), challenge: url.searchParams.get("code_challenge"), failure: url.searchParams.get("fixture_failure") });
    redirect.searchParams.set("code", code);
    redirect.searchParams.set("state", url.searchParams.get("state"));
    response.writeHead(302, { Location: redirect.href });
    response.end();
    return;
  }
  if (request.method === "POST" && url.pathname === prefix + "/oauth2/v2.0/token") {
    let body = "";
    for await (const part of request) { body += part.toString(); assert.ok(body.length <= 16384); }
    const form = new URLSearchParams(body);
    assert.equal(form.get("client_id"), input.portalClient);
    assert.ok(form.get("client_secret") === input.clientSecret, "The configured confidential client must authenticate.");
    assert.equal(form.get("grant_type"), "authorization_code");
    const grant = codes.get(form.get("code"));
    assert.ok(grant);
    codes.delete(form.get("code"));
    assert.equal(form.get("redirect_uri"), grant.redirect);
    assert.equal(createHash("sha256").update(form.get("code_verifier")).digest("base64url"), grant.challenge);
    const now = Math.floor(Date.now() / 1000);
    const common = { iss: issuer, tid: input.tenant, oid: grant.subject, iat: now, nbf: now - 5, exp: now + 600, ver: "2.0" };
    const access = jwt({ ...common, aud: input.audience, sub: "pairwise-" + grant.subject,
      azp: input.portalClient, scp: "management.operator", jti: randomUUID() });
    const id = jwt({ ...common, iss: grant.failure === "issuer" ? issuer + "/wrong" : issuer,
      aud: grant.failure === "audience" ? input.audience : input.portalClient,
      sub: "pairwise-" + grant.subject, nonce: grant.failure === "nonce" ? "wrong-nonce" : grant.nonce,
      name: grant.subject === input.operatorA ? "Operator A" : "Operator B" }, grant.failure === "signature");
    issuedTokens.push({ access, id });
    response.writeHead(200, { "Content-Type": "application/json" });
    response.end(JSON.stringify({ access_token: access, id_token: id, token_type: "Bearer", expires_in: 600,
      scope: `openid profile api://${input.audience}/management.operator`, refresh_token: "fixture-unrequested-refresh-token" }));
    return;
  }
  response.writeHead(404);
  response.end();
}
function jwt(payload, corrupt = false) {
  const header = Buffer.from(JSON.stringify({ alg: "RS256", typ: "JWT", kid: "rm09-portal-owned-key" })).toString("base64url");
  const content = header + "." + Buffer.from(JSON.stringify(payload)).toString("base64url");
  const signature = sign("sha256", Buffer.from(content), { key: signing, padding: constants.RSA_PKCS1_PADDING });
  if (corrupt) { signature[0] ^= 1; }
  return content + "." + signature.toString("base64url");
}
async function unusedPort() {
  const probe = net.createServer();
  await new Promise((resolve, reject) => { probe.once("error", reject); probe.listen(0, "127.0.0.1", resolve); });
  const port = probe.address().port;
  await new Promise((resolve, reject) => probe.close(error => error ? reject(error) : resolve()));
  return port;
}
