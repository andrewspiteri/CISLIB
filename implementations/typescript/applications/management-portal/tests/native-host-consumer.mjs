// Named RM09 integration fixture: real Auth.js, PostgreSQL sessions and native Management.
// Its small signed issuer exists only in tests; production contains no issuer/mock mode.
import assert from "node:assert/strict";
import { createHash, createPrivateKey, createPublicKey, randomBytes, randomUUID, sign, constants } from "node:crypto";
import { readFile, rename } from "node:fs/promises";
import { mkdirSync, writeFileSync } from "node:fs";
import { fork } from "node:child_process";
import https from "node:https";
import net from "node:net";
import { join, dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { chromium, request as playwrightRequest } from "@playwright/test";
import { verifySessionStoreDrain } from "./native-session-store-consumer.mjs";
import { captureManualSession, fixtureTokenLifetimeSeconds, localLoginPage, waitForManualContinuityProbe, waitForManualStop } from "./manual-session.mjs";
import { captureStoreConfig } from "../src/server/config.ts";
import { createSessionStore } from "../src/server/store.ts";
import { capturePortalContext, portalContextHeader, portalContextKey } from "../src/portal-context.ts";

const directory = process.argv[2];
assert.ok(directory, "The existing native fixture supplies a newly owned private directory.");
const input = JSON.parse(await readFile(join(directory, "portal-fixture.json"), "utf8"));
const configurationBoundary = input.configurationBoundary;
const instanceStatusBoundaries = input.instanceStatusBoundaries;
const configurationMenus = input.configurationMenus;
const editorRefresh = input.editorRefresh;
const expectedApplicationsA = configurationBoundary?.scenario === "Predeployment"
  ? [input.applicationA, configurationBoundary.applicationId] : [input.applicationA];
const hasBoundaryJourney = configurationBoundary != null || instanceStatusBoundaries != null || configurationMenus != null || editorRefresh != null || input.sessionTabs === true;
const manual = captureManualSession(input.manual);
if (manual !== undefined) {
  assert.match(input.continuityOperator, /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/u);
  assert.ok(input.continuityOperator !== input.operatorA && input.continuityOperator !== input.operatorB);
}
const fixtureSubjects = Object.freeze(manual === undefined ? [input.operatorA, input.operatorB, input.lookupOperator,
  ...(input.refreshOperator == null ? [] : [input.refreshOperator])]
  : [input.operatorA, input.operatorB, input.continuityOperator]);
const portalDirectory = dirname(dirname(fileURLToPath(import.meta.url)));
const evidenceDirectory = join(resolve(portalDirectory, "../../../.."), ".artifacts", "management-portal", "CIS-0029",
  manual === undefined ? "WORK-004" : "WORK-006", "native-host-" + randomUUID());
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
let manualBrowser;
let manualBrowserClosed = true;
let manualStopReason;
let manualTokenExchanges = 0;
let manualStatus;
let issuerClosed = false;
let manualPortalOrigin;
let manualLeaseExpiresAtMs;
const contexts = [];
const closedApiContexts = new Set();
const cases = [];
const failures = [];
const phaseHistory = [];
const observations = {};
const metadataAppliedKeys = [];
const metadataConflictKeys = [];
let configurationProof;
let rolloutProof;
let r08Proof;
let configurationRepairsProof;
let versionWorkflowProof;
let instanceStatusProof;
let firstVersionProof;
let shutdownProbe;
let canceledReadProbe;
let configurationSaveProof;
let releaseConflictProof;
let configurationDeployProof;
let configurationBoundaryProof;
let instanceStatusBoundariesProof;
let configurationMenusProof;
let sessionTabsProof;
let editorRefreshProof;
recordPhase("preparation");
if (manual !== undefined) { await publishManualStatus("starting"); }
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
  if (manual === undefined && input.hostShutdown !== true && input.canceledHostRead !== true) {
    cases.push(...await verifySessionStoreDrain({ owner, databaseUrl: input.sessionRuntimeUrl, schema: input.sessionSchema,
      reportPhase: recordPhase }));
  }
  recordPhase("issuer-listen");
  await new Promise((resolve, reject) => { authority.once("error", reject); authority.listen(input.issuerPort, "127.0.0.1", resolve); });
  const port = await unusedPort();
  const origin = `https://127.0.0.1:${port}`;
  if (manual !== undefined) { manualPortalOrigin = origin; }
  const environment = { ...process.env,
    NEXTAUTH_URL: origin, NEXTAUTH_SECRET: input.authSecret,
    CIS_PORTAL_MANAGEMENT_URL: input.managementUrl, CIS_PORTAL_MANAGEMENT_CA_FILE: join(directory, "management-root.pem"),
    CIS_PORTAL_TENANT_ID: input.tenant, CIS_PORTAL_CLIENT_ID: input.portalClient,
    CIS_PORTAL_CLIENT_SECRET: input.clientSecret, CIS_PORTAL_MANAGEMENT_AUDIENCE: input.audience,
    CIS_PORTAL_OPERATOR_SCOPE: "management.operator", CIS_PORTAL_ALLOWED_SUBJECTS: fixtureSubjects.join(","),
    CIS_PORTAL_AUTHORITY_ORIGIN: input.authority, CIS_PORTAL_SESSION_DATABASE: input.sessionRuntimeUrl,
    CIS_PORTAL_SESSION_SCHEMA: input.sessionSchema, CIS_PORTAL_BIND_ADDRESS: "127.0.0.1", CIS_PORTAL_PORT: String(port),
    CIS_PORTAL_SERVER_CERTIFICATE_FILE: join(directory, "server.pem"), CIS_PORTAL_SERVER_KEY_FILE: join(directory, "server.key") };
  delete environment.NEXTAUTH_URL_INTERNAL;
  host = fork(join(portalDirectory, "server.mts"), [], { cwd: portalDirectory, env: environment,
    stdio: ["ignore", "pipe", "pipe", "ipc"], windowsHide: true });
  host.stdout.on("data", value => {
    hostOutput = (hostOutput + value.toString()).slice(-65536);
    observeHostShutdown();
  });
  host.stderr.on("data", value => { hostErrors = (hostErrors + value.toString()).slice(-65536); });
  // Node 24's aggregate child close count can retain an explicitly disconnected
  // IPC contribution. Join every actual public resource event independently.
  host.once("close", () => { hostClosed = true; writePhase(); });
  const processExitObserved = new Promise(resolve => {
    host.once("exit", (code, signal) => {
      hostExited = true;
      observations.hostExitCode = Number.isSafeInteger(code) ? code : null;
      observations.hostExitSignal = signal === null ? null : ["SIGABRT", "SIGTERM", "SIGINT", "SIGKILL", "SIGSEGV", "SIGILL", "SIGBUS", "SIGTRAP", "SIGBREAK", "SIGHUP", "SIGFPE"].includes(signal) ? signal : "Other";
      writePhase(); resolve({ code, signal });
    });
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
  if (input.hostShutdown === true) {
    cases.push("ordinary-public-health-startup");
  } else if (input.canceledHostRead === true) {
    const a = await newContext(origin);
    await login(a, idp, origin, input.operatorA);
    await assertApplications(a, expectedApplicationsA);
    const { holdCanceledAuthenticatedRead } = await import("./native-host-cancel-consumer.mjs");
    canceledReadProbe = await holdCanceledAuthenticatedRead({ owner, schema: input.sessionSchema, origin,
      ca: await readFile(join(directory, "root.pem")), context: a, applicationId: input.applicationA });
    observations.canceledReadHeldInSessionSql = true;
    cases.push("real-authenticated-read-canceled-during-session-sql");
  } else if (manual !== undefined) {
    await runManualSession(origin, processExitObserved, idp);
    recordPhase("manual-browser-before-host-stop");
    await closeManualReviewBrowser();
    await publishManualStatus("stopping");
  } else {
    const unauthenticated = await anonymous.get("/api/portal/applications");
    await assertUnauthenticated(unauthenticated);
    assert.match(unauthenticated.headers()["cache-control"], /no-store/u);
    cases.push("unauthenticated-no-store");

    recordPhase("valid-operator-a");
    const a = await newContext(origin);
    await login(a, idp, origin, input.operatorA);
    const contextA = await assertApplications(a, expectedApplicationsA);
    assert.deepEqual(await assertApplications(a, expectedApplicationsA), contextA, "One validated database session keeps stable browser-safe associations.");
    const instanceResponse = await a.get(`/api/portal/applications/${input.applicationA}/instances`);
    assert.equal(instanceResponse.status(), 200);
    const instances = await instanceResponse.json();
    assert.equal(instances.state, "ready");
    assert.deepEqual(responseContext(instances), contextA);
    if (instanceStatusBoundaries != null && instances.value.nextCursor !== undefined) {
      const continuation = await a.get(`/api/portal/applications/${input.applicationA}/instances?cursor=${encodeURIComponent(instances.value.nextCursor)}`);
      try {
        assert.equal(continuation.status(), 200);
        const next = await continuation.json();
        assert.equal(next.state, "ready");
        assert.deepEqual(responseContext(next), contextA);
        assert.equal(next.value.nextCursor, undefined, "The owned status fixture has at most two ordinary instance pages.");
        instances.value.items.push(...next.value.items);
      } finally { await continuation.dispose(); }
    }
    assert.equal(instances.value.items.find(item => item.registrationId === input.registrationA)?.applicationVersion, "1.0.0");
    assert.equal(instances.value.items.find(item => item.registrationId === input.secondRegistration)?.applicationVersion, "2.4.2");
    const inventoryResponse = await a.get(`/api/portal/applications/${input.applicationA}/inventory`);
    assert.equal(inventoryResponse.status(), 200);
    const inventory = await inventoryResponse.json();
    assert.equal(inventory.state, "ready");
    assert.deepEqual(responseContext(inventory), contextA);
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
    assert.deepEqual(responseContext(definitions), contextA);
    const sharedBoundary = configurationBoundary?.scenario === "SharedReferenceDiff" || configurationBoundary?.scenario === "SessionStorage" || editorRefresh != null || input.sessionTabs === true;
    assert.equal(definitions.value.items.length, configurationMenus != null ? configurationMenus.rows.length
      : input.reviewCorrections === true || input.versionWorkflow === true || input.releaseConflict === true || sharedBoundary ? 2 : 1);
    const typedDefinitions = definitions.value.items.filter(item => item.configurationId === "shared.settings");
    assert.equal(typedDefinitions.length, 1);
    assert.equal(typedDefinitions[0].hostApplicationName, "FirstHost");
    if (input.reviewCorrections === true || input.versionWorkflow === true || input.releaseConflict === true || sharedBoundary) {
      assert.deepEqual(definitions.value.items.map(item => item.configurationId).sort(), ["shared.features", "shared.settings"]);
    }
    const capabilitiesResponse = await a.get(`/api/portal/applications/${input.applicationA}/capabilities`);
    assert.equal(capabilitiesResponse.status(), 200);
    const capabilities = await capabilitiesResponse.json();
    assert.equal(capabilities.state, "ready");
    assert.deepEqual(responseContext(capabilities), contextA);
    assert.equal(capabilities.value.catalog.state, "unsupported");
    assert.ok(capabilities.value.serviceApis.some(item => item.operationId === "registerInstance"));
    assert.equal("implementedApis" in capabilities.value, false, "The private portal response separates diagnostics from an application feature catalog.");
    assert.equal("jobs" in capabilities.value, false, "Inventory cannot create trusted capability definitions.");
    cases.push("per-registration-version-and-exact-inventory", "genuine-metadata-only-config-definition");
    cases.push("actual-missing-feature-catalog-with-separate-api-diagnostics");
    const cookieA = (await a.storageState()).cookies.find(cookie => cookie.name === "__Host-cis-portal.session-token");
    assert.ok(cookieA?.secure && cookieA.httpOnly && !cookieA.value.includes("."), "Only an opaque secure HttpOnly session ticket reaches the browser.");
    const denied = await a.get(`/api/portal/applications/${input.applicationB}`);
    assert.ok([403, 404].includes(denied.status()), "The native target grant must hide or deny the other application.");
    cases.push("real-code-pkce-id-token-native-grant", "opaque-session-cookie", "native-target-denial", "stable-protected-session-associations");

    recordPhase("valid-operator-b");
    const b = await newContext(origin);
    await login(b, idp, origin, input.operatorB);
    const contextB = await assertApplications(b, [input.applicationB]);
    assert.notEqual(contextB.operatorId, contextA.operatorId);
    assert.notEqual(contextB.sessionAssociation, contextA.sessionAssociation);
    assert.equal(contextB.serviceAssociation, contextA.serviceAssociation);
    await assertApplications(a, expectedApplicationsA);
    const wrongContext = await a.get("/api/portal/applications", { headers: { [portalContextHeader]: portalContextKey(contextB) } });
    assert.equal(wrongContext.status(), 409);
    const wrongBody = await wrongContext.json();
    assert.equal(wrongBody.state, "invalid");
    assert.equal("value" in wrongBody, false);
    assert.deepEqual(responseContext(wrongBody), contextA);
    cases.push("independent-user-session-isolation", "foreign-context-stamp-has-no-protected-data");
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
    await assertApplications(a, expectedApplicationsA);
    cases.push("active-session-cannot-link-another-account");

    recordPhase("closed-auth-inputs");
    assert.equal((await anonymous.post("/api/auth/signin/cis-management", {
      form: { csrfToken: "untrusted", scope: "arbitrary" }, headers: { Origin: origin } })).status(), 400);
    assert.equal((await a.post("/api/auth/signout", {
      form: { csrfToken: activeCsrf }, headers: { Origin: "https://other.example" } })).status(), 403);
    await assertApplications(a, expectedApplicationsA);
    cases.push("scope-override-and-cross-origin-signout-denied");

    recordPhase("native-metadata-authoring-and-original-recovery");
    const lookupProof = await verifyMetadata(a, b, idp, origin, contextA, contextB);
    async function verifyDesktop() {
      recordPhase("desktop-browser");
      let sessionTabsContexts;
      let refreshOperatorContext;
      if (input.sessionTabs === true) {
        const initial = await newContext(origin);
        await login(initial, idp, origin, input.operatorA);
        const renewed = await newContext(origin);
        await login(renewed, idp, origin, input.operatorA);
        sessionTabsContexts = { initial, renewed };
      }
      if (editorRefresh != null) {
        refreshOperatorContext = await newContext(origin);
        await login(refreshOperatorContext, idp, origin, input.refreshOperator);
      }
      const { verifyBrowser } = await import("./native-browser-checks.mjs");
      const desktop = await verifyBrowser({ origin, applicationA: input.applicationA, applicationB: input.applicationB,
        registrationA: input.registrationA, operatorAContext: a, operatorBContext: b, lookupOperatorContext: lookupProof.lookup,
        directory, rollout: input.rollout, adoptionObservations: input.adoptionObservations, r08: input.r08 === true,
        reviewCorrections: input.reviewCorrections === true, versionWorkflow: input.versionWorkflow === true,
        instanceStatus: input.instanceStatus === true, firstVersion: input.firstVersion === true,
        newSaveRecovery: input.newSaveRecovery === true, releaseConflict: input.releaseConflict === true,
        configurationDeploy: input.configurationDeploy, configurationBoundary, instanceStatusBoundaries,
        configurationMenus, sessionTabsContexts, editorRefresh, refreshOperatorContext });
      metadataAppliedKeys.push(...desktop.metadataAppliedKeys);
      metadataConflictKeys.push(...desktop.metadataConflictKeys);
      configurationProof = desktop.configuration;
      rolloutProof = desktop.rollout;
      r08Proof = desktop.r08;
      configurationRepairsProof = desktop.configurationRepairs;
      versionWorkflowProof = desktop.versionWorkflow;
      instanceStatusProof = desktop.instanceStatus;
      firstVersionProof = desktop.firstVersion;
      configurationSaveProof = desktop.configurationSave;
      releaseConflictProof = desktop.releaseConflict;
      configurationDeployProof = desktop.configurationDeploy;
      configurationBoundaryProof = desktop.configurationBoundary;
      instanceStatusBoundariesProof = desktop.instanceStatusBoundaries;
      configurationMenusProof = desktop.configurationMenus;
      sessionTabsProof = desktop.sessionTabs;
      editorRefreshProof = desktop.editorRefresh;
    }
    // Exercise this changed UI first while retaining every legacy API assertion
    // below. A later native crash still fails the test, but cannot erase saved
    // browser evidence from the already completed version scenario.
    const configurationAutomation = input.newSaveRecovery === true || input.releaseConflict === true || input.configurationDeploy !== null && input.configurationDeploy !== undefined;
    if (input.configurationDeploy !== null && input.configurationDeploy !== undefined) {
      const { verifyConfigurationDeployApi } = await import("./native-config-deploy-consumer.mjs");
      await verifyConfigurationDeployApi({ a, b, origin, input, contextA, contextB, cases, reportPhase: recordPhase });
    }
    if (input.browser && (input.versionWorkflow === true || input.instanceStatus === true || input.firstVersion === true || configurationAutomation || hasBoundaryJourney)) { await verifyDesktop(); }
    if (hasBoundaryJourney) {
      cases.push("focused-boundary-retains-native-auth-and-metadata-without-legacy-authoring");
    } else if (configurationAutomation) {
      cases.push("configuration-automation-retains-ordinary-auth-and-metadata-preludes");
    } else if (input.firstVersion === true) {
      cases.push("focused-first-version-retains-empty-binding-without-configuration-lookup-seed");
    } else if (input.instanceStatus === true) {
      cases.push("focused-instance-status-has-no-configuration-authoring-continuation");
    } else if (input.rollout === null || input.rollout === undefined) {
      const { verifyConfigurationApi } = await import("./native-configuration-checks.mjs");
      recordPhase("native-configuration-authority-and-original-recovery");
      await verifyConfigurationApi({ a, b, lookup: lookupProof.lookup, lookupContext: lookupProof.context, origin, input, contextA, contextB, cases, reportPhase: recordPhase });
    } else {
      const { verifyRolloutApi } = await import("./native-rollout-checks.mjs");
      recordPhase("native-assignment-authority-and-original-recovery");
      await verifyRolloutApi({ a, b, lookup: lookupProof.lookup, lookupContext: lookupProof.context, origin, input, contextA, contextB, cases, reportPhase: recordPhase });
    }

    if (input.browser && input.versionWorkflow !== true && input.instanceStatus !== true && input.firstVersion !== true && !configurationAutomation && !hasBoundaryJourney) { await verifyDesktop(); }

    recordPhase("expired-session-observe-clocks");
    const nodeNow = Date.now();
    const databaseClock = await owner.query("SELECT extract(epoch FROM clock_timestamp())*1000 AS milliseconds");
    observations.expiryDatabaseMinusNodeMilliseconds = Math.round(Number(databaseClock.rows[0].milliseconds) - nodeNow);
    recordPhase("expired-session-update-row");
    const expired = await owner.query('UPDATE sessions SET expires=$3 WHERE "sessionToken"=$4 AND "userId" IN (SELECT "userId" FROM accounts WHERE provider=$1 AND "providerAccountId"=$2) RETURNING expires',
      ["cis-management", input.operatorA, new Date(Date.now() - 60000), cookieA.value]);
    observations.expiryUpdatedRows = expired.rowCount;
    const expiredDeadline = expired.rows[0]?.expires instanceof Date ? expired.rows[0].expires.getTime() : NaN;
    observations.expiryReturnedDeadlineMilliseconds = Number.isFinite(expiredDeadline) ? expiredDeadline : null;
    writePhase();
    assert.equal(expired.rowCount, 1, "The fixture must expire exactly the intended existing session row.");
    assert.ok(Number.isFinite(expiredDeadline) && expiredDeadline < Date.now(), "The persisted session deadline must be past on the validator's Node clock.");
    recordPhase("expired-session-protected-read");
    const expiredRead = await a.get("/api/portal/applications", { headers: { [portalContextHeader]: portalContextKey(contextA) } });
    observations.expiredSessionHttpStatus = expiredRead.status();
    writePhase();
    await assertUnauthenticated(expiredRead);
    recordPhase("expired-session-and-reauthentication");
    const before = issuedTokens.length;
    await login(a, idp, origin, input.operatorA);
    const reauthenticatedA = await assertApplications(a, expectedApplicationsA);
    assert.equal(reauthenticatedA.operatorId, contextA.operatorId);
    assert.equal(reauthenticatedA.serviceAssociation, contextA.serviceAssociation);
    assert.notEqual(reauthenticatedA.sessionAssociation, contextA.sessionAssociation);
    const staleSession = await a.get("/api/portal/applications", { headers: { [portalContextHeader]: portalContextKey(contextA) } });
    assert.equal(staleSession.status(), 409);
    const staleBody = await staleSession.json();
    assert.equal(staleBody.state, "invalid");
    assert.equal("value" in staleBody, false);
    assert.deepEqual(responseContext(staleBody), reauthenticatedA);
    assert.ok(issuedTokens.length > before);
    const actual = await owner.query('SELECT access_token FROM accounts WHERE provider=$1 AND "providerAccountId"=$2', ["cis-management", input.operatorA]);
    assert.ok(actual.rows[0].access_token === issuedTokens.at(-1).access, "Validated reauthentication updates only that account's server-side credential.");
    cases.push("real-postgres-session-expiry", "reauthentication-updates-same-account", "same-user-reauthentication-retires-session-association");

    recordPhase("revocation-and-signout");
    await owner.query('DELETE FROM sessions WHERE "userId" IN (SELECT "userId" FROM accounts WHERE provider=$1 AND "providerAccountId"=$2)', ["cis-management", input.operatorA]);
    await assertUnauthenticated(await a.get("/api/portal/applications", { headers: { [portalContextHeader]: portalContextKey(reauthenticatedA) } }));
    await assertApplications(b, [input.applicationB]);
    const signoutCsrf = (await (await b.get("/api/auth/csrf")).json()).csrfToken;
    assert.equal((await b.post("/api/auth/signout", { form: { csrfToken: signoutCsrf, callbackUrl: origin + "/applications", json: "true" },
      headers: { Origin: origin }, maxRedirects: 0 })).status(), 200);
    await assertUnauthenticated(await b.get("/api/portal/applications", { headers: { [portalContextHeader]: portalContextKey(contextB) } }));
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
  }
  if (input.firstVersion === true || input.hostShutdown === true) {
    shutdownProbe = await openIncompleteTlsProbe(origin);
    recordPhase("incomplete-tls-peer-held");
  }
  recordPhase("api-contexts-before-host-stop");
  await closeOwnedApiContexts();
  recordPhase("graceful-host-stop");
  if (manual !== undefined) { await publishManualStatus("stopping"); }
  if (shutdownProbe !== undefined) {
    assert.equal(shutdownProbe.closed, false, "The owned incomplete TLS peer must remain open until host stop begins.");
    shutdownProbe.stopInitiated = true;
  }
  host.disconnect();
  if (canceledReadProbe !== undefined) {
    try {
      const until = Date.now() + 3000;
      while (observations.hostShutdownPhase !== "handlers" && Date.now() < until) {
        await new Promise(resolve => setTimeout(resolve, 10));
      }
      assert.equal(observations.hostShutdownPhase, "handlers", "Stop must retain the actual SQL-blocked handler.");
      assert.ok(observations.hostShutdownHandlers > 0 && observations.hostShutdownRequests > 0);
      observations.canceledReadRetainedDuringStop = true;
    } finally { await canceledReadProbe.release(); }
  }
  if (shutdownProbe !== undefined) { await verifyIncompleteTlsStop(shutdownProbe); }
  const exited = await hostExit;
  assert.equal(exited.code, 0, "The host must finish actual cleanup after parent disconnect.");
  hostStopped = true;
  cases.push("awaited-host-owner-stop");
} catch (error) {
  rememberFailure(error);
} finally {
  if (canceledReadProbe !== undefined) {
    try { await canceledReadProbe.release(); } catch (error) { rememberFailure(error); }
  }
  if (shutdownProbe !== undefined) {
    try { await restoreIncompleteTlsProbe(shutdownProbe); } catch (error) { rememberFailure(error); }
  }
  if (manualBrowser !== undefined && !manualBrowserClosed) {
    recordPhase("cleanup-manual-browser");
    try { await closeManualReviewBrowser(); } catch (error) { rememberFailure(error); }
  }
  recordPhase("cleanup-api-contexts");
  try { await closeOwnedApiContexts(); } catch (error) { rememberFailure(error); }
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
    issuerClosed = true;
  } catch (error) { rememberFailure(error); }
}
if (failures.length !== 0) {
  recordPhase("failed");
  if (manual !== undefined) { await publishManualStatus("failed"); }
  writeFileSync(join(evidenceDirectory, "report.json"), JSON.stringify({ completed: false, cases, firstFailurePhase,
    failureCount: failures.length, failureDetails: failures.slice(0, 8).map(fixtureFailure), observations, hostReady, hostStopped, hostExited, hostStdoutClosed, hostStderrClosed,
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
  if (manual !== undefined) { await publishManualStatus("closed"); }
  const report = { cases, nativeOidc: input.hostShutdown === true ? false : manual === undefined || manualTokenExchanges > 0,
    hostShutdown: input.hostShutdown === true, sessionsRealPostgres: true, tokensStayedServerSide: true,
    observations, metadataAppliedKeys, metadataConflictKeys, ...(configurationProof === undefined ? {} : { configuration: configurationProof }), ...(rolloutProof === undefined ? {} : { rollout: rolloutProof }), ...(r08Proof === undefined ? {} : { r08: r08Proof }),
    ...(configurationRepairsProof === undefined ? {} : { configurationRepairs: configurationRepairsProof }),
    ...(versionWorkflowProof === undefined ? {} : { versionWorkflow: versionWorkflowProof }),
    ...(instanceStatusProof === undefined ? {} : { instanceStatus: instanceStatusProof }),
    ...(firstVersionProof === undefined ? {} : { firstVersion: firstVersionProof }), hostStopped, hostExited, hostStdoutClosed, hostStderrClosed, hostDisconnected, hostNativeJoined,
    ...(configurationSaveProof === undefined ? {} : { configurationSave: configurationSaveProof }),
    ...(releaseConflictProof === undefined ? {} : { releaseConflict: releaseConflictProof }),
    ...(configurationDeployProof === undefined ? {} : { configurationDeploy: configurationDeployProof }),
    ...(configurationBoundaryProof === undefined ? {} : { configurationBoundary: configurationBoundaryProof }),
    ...(instanceStatusBoundariesProof === undefined ? {} : { instanceStatusBoundaries: instanceStatusBoundariesProof }),
    ...(configurationMenusProof === undefined ? {} : { configurationMenus: configurationMenusProof }),
    ...(sessionTabsProof === undefined ? {} : { sessionTabs: sessionTabsProof }),
    ...(editorRefreshProof === undefined ? {} : { editorRefresh: editorRefreshProof }),
    hostClosed, socketsDrained: true, browserChecked: manual === undefined && input.browser,
    ...(manual === undefined ? {} : { manualSession: true, manualStopReason, manualTokenExchanges, manualBrowserClosed }) };
  writeFileSync(join(evidenceDirectory, "report.json"), JSON.stringify(report, null, 2));
  process.stdout.write(JSON.stringify(report));
}

async function verifyMetadata(a, b, idp, origin, contextA, contextB) {
  const path = `/api/portal/applications/${input.applicationA}/metadata`;
  const current = await a.get(path);
  assert.equal(current.status(), 200);
  const before = await current.json();
  assert.equal(before.state, "ready"); assert.equal(before.value.canReplace, true);
  assert.deepEqual(responseContext(before), contextA);
  const headers = { Origin: origin, "Content-Type": "application/json", [portalContextHeader]: portalContextKey(contextA),
    "If-Match": before.value.metadataETag };
  const command = { commandVersion: 1, displayName: "Metadata API proof", description: "Description to clear" };
  assert.equal((await a.put(path, { headers: { ...headers, Origin: "https://other.example", "Idempotency-Key": randomUUID() }, data: command })).status(), 403);
  const unstamped = { ...headers, "Idempotency-Key": randomUUID() }; delete unstamped[portalContextHeader];
  assert.equal((await a.put(path, { headers: unstamped, data: command })).status(), 400);
  for (const raw of ['{"commandVersion":1.0,"displayName":"Rejected"}', '{"commandVersion":1e0,"displayName":"Rejected"}',
    '{"commandVersion":1,"displayName":"one","displayName":"two"}', '{"commandVersion":1,"displayName":"Rejected","description":null}']) {
    assert.equal((await a.put(path, { headers: { ...headers, "Idempotency-Key": randomUUID() }, data: raw })).status(), 400);
  }
  const appliedKey = randomUUID();
  const accepted = await a.put(path, { headers: { ...headers, "Idempotency-Key": appliedKey }, data: command });
  const applied = await accepted.json();
  assert.equal(accepted.status(), 200); assert.equal(applied.state, "ready");
  assert.equal(applied.value.disposition, "applied"); assert.equal(applied.value.key, appliedKey);
  assert.equal(applied.value.originalCompletion, "committed"); assert.equal(applied.value.decision.metadata.description, command.description);
  metadataAppliedKeys.push(appliedKey);
  const metadataConflictKey = randomUUID();
  metadataConflictKeys.push(metadataConflictKey);
  const stale = await a.put(path, { headers: { ...headers, "Idempotency-Key": metadataConflictKey }, data: { commandVersion: 1, displayName: "Stale intent" } });
  const conflict = await stale.json();
  assert.equal(conflict.state, "ready"); assert.equal(conflict.value.disposition, "conflict");
  assert.equal(conflict.value.originalCompletion, "committed"); assert.equal(conflict.value.decision, undefined);
  assert.equal(conflict.value.rejection.code, "management.precondition_failed");
  const found = await a.get(path + "-operations/" + appliedKey, { headers: { [portalContextHeader]: portalContextKey(contextA) } });
  const original = await found.json();
  assert.equal(original.value.disposition, "applied"); assert.equal(original.value.resultSource, "found");
  assert.deepEqual(original.value.decision, applied.value.decision);
  const resetKey = randomUUID();
  const reset = await a.put(path, { headers: { ...headers, "If-Match": applied.value.decision.metadataETag, "Idempotency-Key": resetKey },
    data: { commandVersion: 1, displayName: before.value.metadata.displayName, description: "Description to clear" } });
  assert.equal((await reset.json()).value.disposition, "applied"); metadataAppliedKeys.push(resetKey);

  const readonly = await b.get(`/api/portal/applications/${input.applicationB}/metadata`);
  const readonlyBody = await readonly.json();
  assert.equal(readonlyBody.state, "ready"); assert.equal(readonlyBody.value.canReplace, false);
  const refused = await b.put(`/api/portal/applications/${input.applicationB}/metadata`, { headers: { Origin: origin, "Content-Type": "application/json",
    [portalContextHeader]: portalContextKey(contextB), "If-Match": readonlyBody.value.metadataETag, "Idempotency-Key": randomUUID() }, data: command });
  const refusedBody = await refused.json();
  assert.equal(refusedBody.value.disposition, "unresolved"); assert.ok([403, 404].includes(refusedBody.value.transport.status));
  assert.equal(refusedBody.value.decision, undefined);

  const lookup = await newContext(origin);
  await login(lookup, idp, origin, input.lookupOperator);
  const unreadable = await lookup.get(path);
  assert.ok([403, 404].includes(unreadable.status()));
  const lookupContext = responseContext(await unreadable.json());
  const permitted = await lookup.get(path + "-operations/" + input.lookupOriginalKey, { headers: { [portalContextHeader]: portalContextKey(lookupContext) } });
  const recovered = await permitted.json();
  assert.equal(recovered.state, "ready"); assert.equal(recovered.value.disposition, "applied");
  assert.equal(recovered.value.key, input.lookupOriginalKey); assert.equal(recovered.value.resultSource, "found");
  const foreign = await lookup.get(path + "-operations/" + appliedKey, { headers: { [portalContextHeader]: portalContextKey(lookupContext) } });
  const absent = await foreign.json();
  assert.equal(absent.value.disposition, "unresolved"); assert.equal(absent.value.decision, undefined);
  const removedWrite = await lookup.put(path, { headers: { Origin: origin, "Content-Type": "application/json", [portalContextHeader]: portalContextKey(lookupContext),
    "If-Match": before.value.metadataETag, "Idempotency-Key": randomUUID() }, data: command });
  const removedBody = await removedWrite.json();
  assert.equal(removedBody.value.disposition, "unresolved"); assert.ok([403, 404].includes(removedBody.value.transport.status));
  cases.push("native-metadata-csrf-context-and-exact-command-grammar", "native-metadata-commit-stale-etag-and-original-result",
    "native-read-only-user-cannot-mutate", "native-same-actor-lookup-survives-removed-read-and-write-grants", "native-lookup-does-not-borrow-another-actor-original");
  return { lookup, context: lookupContext };
}

function recordPhase(value) { phase = value; phaseHistory.push(value); if (phaseHistory.length > 32) { phaseHistory.shift(); } writePhase(); }
async function openIncompleteTlsProbe(origin) {
  const target = new URL(origin);
  assert.equal(target.protocol, "https:");
  assert.equal(target.hostname, "127.0.0.1");
  const port = Number(target.port);
  assert.ok(Number.isInteger(port) && port >= 1 && port <= 65535);
  const socket = net.createConnection({ host: "127.0.0.1", port });
  const probe = { socket, closed: false, restoring: false, stopInitiated: false, serverClosedDuringStop: false };
  // Observe native close independently of connect/write completion, including failure restoration.
  probe.closedPromise = new Promise(resolve => socket.once("close", () => {
    probe.closed = true;
    probe.serverClosedDuringStop = probe.stopInitiated && !probe.restoring;
    resolve();
  }));
  // An expected native reset still closes the actual socket; no raw error is persisted.
  socket.on("error", () => {});
  try {
    await new Promise((resolve, reject) => {
      const connected = () => { socket.off("error", failed); resolve(); };
      const failed = error => { socket.off("connect", connected); reject(error); };
      socket.once("connect", connected);
      socket.once("error", failed);
    });
    await new Promise((resolve, reject) => socket.write(Buffer.from([0x16]), error => error ? reject(error) : resolve()));
    assert.equal(probe.closed, false, "The owned probe must hold an incomplete TLS record without sending HTTP or credentials.");
    return probe;
  } catch (error) {
    await restoreIncompleteTlsProbe(probe);
    throw error;
  }
}
async function verifyIncompleteTlsStop(probe) {
  let watchdogFailure;
  const timer = setTimeout(() => {
    watchdogFailure = new Error("Host stop did not close its incomplete TLS peer within the fixture watchdog.");
    probe.restoring = true;
    probe.socket.destroy();
  }, 5000);
  try {
    await probe.closedPromise;
    if (watchdogFailure !== undefined) { throw watchdogFailure; }
    assert.equal(probe.serverClosedDuringStop, true, "The actual server must close the peer before fixture restoration destroys it.");
    observations.hostClosedIncompleteTlsPeer = true;
    cases.push("native-host-stop-closes-owned-incomplete-tls-peer");
    writePhase();
  } finally { clearTimeout(timer); }
}
async function restoreIncompleteTlsProbe(probe) {
  if (!probe.closed) {
    probe.restoring = true;
    probe.socket.destroy();
  }
  await probe.closedPromise;
}
function observeHostShutdown() {
  // Parse only complete fixed lifecycle lines from the already bounded stdout tail.
  // No raw stdout, dynamic phase text or request/credential data enters observations.
  const markers = /^\[Portal\] Shutdown phase=(transport|handlers|next|runtime|complete) handlers=(0|[1-9][0-9]?)(?: connections=(0|[1-9][0-9]?))?(?: requests=([0-8]))?\r?\n/gmu;
  let latest;
  for (const marker of hostOutput.matchAll(markers)) {
    const handlers = Number(marker[2]);
    const connections = marker[3] === undefined ? undefined : Number(marker[3]);
    const requests = marker[4] === undefined ? undefined : Number(marker[4]);
    if (Number.isInteger(handlers) && handlers >= 0 && handlers <= 64
      && (connections === undefined || Number.isInteger(connections) && connections >= 0 && connections <= 64)) {
      latest = { phase: marker[1], handlers, connections, requests };
    }
  }
  if (latest !== undefined && (observations.hostShutdownPhase !== latest.phase || observations.hostShutdownHandlers !== latest.handlers
    || observations.hostShutdownConnections !== latest.connections || observations.hostShutdownRequests !== latest.requests)) {
    observations.hostShutdownPhase = latest.phase;
    observations.hostShutdownHandlers = latest.handlers;
    if (latest.connections === undefined) { delete observations.hostShutdownConnections; }
    else { observations.hostShutdownConnections = latest.connections; }
    if (latest.requests === undefined) { delete observations.hostShutdownRequests; }
    else { observations.hostShutdownRequests = latest.requests; }
    writePhase();
  }
  const pendingMarkers = /^\[Portal\] Pending handler kind=(auth-callback|auth-signin|auth-session|auth-signout|auth-csrf|auth-other|portal-api|configuration-page|page|health|asset|other) method=(GET|POST|PUT|PATCH|DELETE|HEAD|OPTIONS|OTHER) finished=(true|false) closed=(true|false) aborted=(true|false) rsc=(true|false)(?: status=([1-5][0-9]{2}) headers=(true|false))?\r?\n/gmu;
  const pending = [];
  const tuples = new Set();
  for (const marker of hostOutput.matchAll(pendingMarkers)) {
    const tuple = marker.slice(1).join("|");
    if (tuples.has(tuple)) { continue; }
    if (pending.length >= 64) { break; }
    tuples.add(tuple);
    pending.push({ kind: marker[1], method: marker[2], finished: marker[3] === "true", closed: marker[4] === "true",
      aborted: marker[5] === "true", rsc: marker[6] === "true",
      ...(marker[7] === undefined ? {} : { status: Number(marker[7]), headers: marker[8] === "true" }) });
  }
  if (pending.length > 0 && JSON.stringify(observations.hostPendingHandlers) !== JSON.stringify(pending)) {
    observations.hostPendingHandlers = pending;
    writePhase();
  }
}
function writePhase() {
  // Only fixed phases/cases and allowlisted error kinds/local source coordinates are persisted, never input or raw errors/logs.
  writeFileSync(join(evidenceDirectory, "phase.json"), JSON.stringify({ phase, phaseHistory, firstFailurePhase, cases, observations,
    failureCount: failures.length, failureDetails: failures.slice(0, 8).map(fixtureFailure), hostSpawned: host !== undefined, hostReady, hostExited, hostStdoutClosed, hostStderrClosed,
    hostDisconnected, hostNativeJoined, hostClosed, hostStopped }, null, 2));
}
function rememberFailure(error) { firstFailurePhase ??= phase; failures.push(error); writePhase(); }
function fixtureFailure(error) {
  const knownKinds = ["Error", "TypeError", "SyntaxError", "AssertionError", "TimeoutError", "AbortError", "AggregateError"];
  const frame = error instanceof Error ? /(native-host-consumer|native-host-cancel-consumer|native-config-save-consumer|native-config-deploy-consumer|native-configuration-menu-checks|native-session-tabs-checks|native-configuration-refresh-checks|native-instance-status-boundaries-checks|native-configuration-boundaries-checks|native-instance-status-checks|native-configuration-versions-checks|native-configuration-checks|native-r08-checks|native-rollout-checks|native-browser-checks|native-session-store-consumer)\.mjs:(\d{1,6}):(\d{1,6})/u.exec(error.stack ?? "") : null;
  return { kind: error instanceof Error && knownKinds.includes(error.name) ? error.name : "Other",
    sourceFile: frame === null ? null : frame[1] + ".mjs", sourceLine: frame === null ? null : Number(frame[2]), sourceColumn: frame === null ? null : Number(frame[3]) };
}

async function publishManualStatus(state, details = {}) {
  manualStatus = { ...manualStatus, ...details, state, issuerUrl: input.authority,
    leaseSeconds: manual.leaseSeconds, accounts: [
      { label: "Operator A", access: "Both sample applications" }, { label: "Operator B", access: "managed-b only" }
    ], browserOpened: manualBrowser !== undefined, browserClosed: manualBrowserClosed,
    stopReason: manualStopReason, fixtureCleanupComplete: false,
    hostReady, hostStopped, hostExited, hostStdoutClosed, hostStderrClosed, hostDisconnected, hostNativeJoined,
    issuerClosed, issuerSocketsDrained: issuerSockets.size === 0 };
  const path = join(manual.controlDirectory, "status.json");
  writeFileSync(path + ".tmp", JSON.stringify(manualStatus, null, 2));
  // Only explicit lifecycle boundaries await publication, so these writes are
  // serialized and native process/socket events never perform status I/O.
  for (let attempt = 0; attempt < 10; attempt++) {
    try {
      await rename(path + ".tmp", path);
      return;
    } catch (error) {
      if (process.platform !== "win32" || !["EPERM", "EACCES", "EBUSY"].includes(error?.code) || attempt === 9) { throw error; }
      await new Promise(resolveDelay => setTimeout(resolveDelay, 50));
    }
  }
}

async function closeOwnedApiContexts() {
  const pending = contexts.filter(context => !closedApiContexts.has(context));
  const results = await Promise.allSettled(pending.map(async context => {
    await context.dispose();
    closedApiContexts.add(context);
  }));
  const errors = results.filter(result => result.status === "rejected").map(result => result.reason);
  if (errors.length > 0) { throw new AggregateError(errors, "The owned API contexts did not all close."); }
}

async function closeManualReviewBrowser() {
  if (manualBrowser === undefined || manualBrowserClosed) { return; }
  await manualBrowser.close();
  manualBrowserClosed = true;
}

async function runManualSession(origin, processExitObserved, idp) {
  manualLeaseExpiresAtMs = Date.now() + manual.leaseSeconds * 1000;
  recordPhase("manual-session-preparing");
  await publishManualStatus("starting", { portalUrl: origin + "/applications" });
  if (manual.openBrowser) {
    // The exception belongs only to this new visible test-browser context. Native
    // issuer/Management transports still validate the explicitly owned CA roots.
    manualBrowser = await chromium.launch({ channel: "msedge", headless: false });
    manualBrowserClosed = false;
    // The user's visible window owns its viewport. A fixed emulated viewport
    // would leave unpainted space after resizing/maximizing the native window.
    const context = await manualBrowser.newContext({ viewport: null, ignoreHTTPSErrors: true });
    const page = await context.newPage();
    await page.goto(origin + "/applications");
  }
  const expiresAt = new Date(manualLeaseExpiresAtMs).toISOString();
  recordPhase("manual-session-ready");
  await publishManualStatus("ready", { expiresAt });
  const remaining = fixtureTokenLifetimeSeconds(manual, manualLeaseExpiresAtMs);
  const stopProbe = new AbortController();
  const failedProbe = new AbortController();
  const probe = remaining < 160 ? publishManualStatus("ready", { continuity: { state: "not-run-short-lease" } })
    : observeManualContinuity(origin, idp, stopProbe.signal);
  // Take custody now, including a rejection while persisting the observer's failure status.
  const probeOutcome = probe.then(() => ({ failed: false }), error => {
    failedProbe.abort(error);
    return { failed: true, error };
  });
  let stopError;
  let observed;
  try {
    manualStopReason = remaining === 0 ? "expired"
      : await waitForManualStop({ ...manual, leaseSeconds: remaining, hostExit: processExitObserved, failureSignal: failedProbe.signal });
  } catch (error) { stopError = error; }
  finally { stopProbe.abort(); observed = await probeOutcome; }
  if (stopError !== undefined && observed.failed && stopError !== observed.error) {
    throw new AggregateError([stopError, observed.error], "The manual stop and continuity owners both failed.");
  }
  if (stopError !== undefined) { throw stopError; }
  if (observed.failed) { throw observed.error; }
  recordPhase("manual-session-stopping");
  await publishManualStatus("stopping");
  cases.push("bounded-manual-session");
}

async function observeManualContinuity(origin, idp, signal) {
  let stage = "initial-read";
  let httpStatus;
  const entered = Date.now();
  try {
    const context = await newContext(origin);
    await login(context, idp, origin, input.continuityOperator);
    const initial = await assertApplications(context, expectedApplicationsA);
    await publishManualStatus("ready", { continuity: { state: "pending", elapsedMs: Date.now() - entered } });
    stage = "elapsed-read";
    if (!await waitForManualContinuityProbe(signal) || signal.aborted) {
      await publishManualStatus("ready", { continuity: { state: "cancelled", elapsedMs: Date.now() - entered } });
      return;
    }
    const response = await context.get("/api/portal/applications", { headers: { [portalContextHeader]: portalContextKey(initial) } });
    httpStatus = response.status();
    assert.equal(httpStatus, 200);
    const body = await response.json();
    assert.equal(body.state, "ready");
    const current = responseContext(body);
    assert.equal(portalContextKey(current), portalContextKey(initial));
    assert.ok(current.expiresAtMs > Date.now());
    cases.push("manual-session-native-read-after-130-seconds");
    await publishManualStatus("ready", { continuity: { state: "passed", elapsedMs: Date.now() - entered, httpStatus } });
  } catch {
    rememberFailure(new Error("MANUAL_SESSION_CONTINUITY_FAILED"));
    await publishManualStatus("ready", { continuity: { state: "failed", stage, elapsedMs: Date.now() - entered,
      ...(httpStatus === undefined ? {} : { httpStatus }) } });
  }
}

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
  const cookies = (await context.storageState()).cookies.filter(cookie => cookie.name === "__Host-cis-portal.session-token");
  for (const value of [input.authSecret, input.clientSecret, ...cookies.map(cookie => cookie.value)]) {
    assert.ok(!text.includes(value), "Session tickets and configured secrets must not enter the protected JSON reply.");
  }
  const body = JSON.parse(text);
  assert.equal(body.state, "ready");
  assert.deepEqual(body.value.items.map(item => item.applicationId).sort(), [...expected].sort());
  assert.match(response.headers()["cache-control"], /no-store/u);
  return responseContext(body);
}

function responseContext(body) {
  const context = capturePortalContext(body.context);
  assert.ok(context && context.expiresAtMs > Date.now(), "Only the current validated session produces a finite opaque context.");
  return context;
}

async function assertUnauthenticated(response) {
  assert.equal(response.status(), 401, "The protected read must reject an absent or expired persisted session.");
  const body = await response.json();
  assert.equal(body.state, "unauthenticated");
  assert.equal("context" in body, false);
  assert.equal("value" in body, false);
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
    if (manual !== undefined) { assert.equal(redirect.origin, manualPortalOrigin); }
    assert.equal(url.searchParams.get("code_challenge_method"), "S256");
    const subject = url.searchParams.get("fixture_subject");
    if (manual !== undefined && subject === null) {
      response.writeHead(200, { "Content-Type": "text/html; charset=utf-8",
        "Content-Security-Policy": `default-src 'none'; form-action 'self' ${redirect.origin}; frame-ancestors 'none'; base-uri 'none'`,
        "Referrer-Policy": "no-referrer", "X-Content-Type-Options": "nosniff" });
      response.end(localLoginPage(url, input.operatorA, input.operatorB));
      return;
    }
    assert.ok(fixtureSubjects.includes(subject));
    if (manual !== undefined) { assert.equal(url.searchParams.get("fixture_failure"), null); }
    if (manual !== undefined) {
      for (const [code, grant] of codes) { if (grant.expiresAt <= Date.now()) { codes.delete(code); } }
    }
    assert.ok(codes.size < 32);
    const code = randomBytes(24).toString("base64url");
    codes.set(code, { subject, redirect: redirect.href, nonce: url.searchParams.get("nonce"),
      state: url.searchParams.get("state"), challenge: url.searchParams.get("code_challenge"), failure: url.searchParams.get("fixture_failure"),
      expiresAt: Date.now() + 120000 });
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
    if (manual !== undefined) { assert.ok(grant.expiresAt > Date.now()); }
    assert.equal(form.get("redirect_uri"), grant.redirect);
    assert.equal(createHash("sha256").update(form.get("code_verifier")).digest("base64url"), grant.challenge);
    const redeemedAt = Date.now();
    const now = Math.floor(redeemedAt / 1000);
    const lifetime = fixtureTokenLifetimeSeconds(manual, manualLeaseExpiresAtMs, redeemedAt);
    if (lifetime <= 30) {
      response.writeHead(400, { "Content-Type": "application/json" });
      response.end(JSON.stringify({ error: "access_denied", error_description: "The local review lease is ending. Start a fresh review session." }));
      return;
    }
    const common = { iss: issuer, tid: input.tenant, oid: grant.subject, iat: now, nbf: now - 5, exp: now + lifetime, ver: "2.0" };
    const access = jwt({ ...common, aud: input.audience, sub: "pairwise-" + grant.subject,
      azp: input.portalClient, scp: "management.operator", jti: randomUUID() });
    const id = jwt({ ...common, iss: grant.failure === "issuer" ? issuer + "/wrong" : issuer,
      aud: grant.failure === "audience" ? input.audience : input.portalClient,
      sub: "pairwise-" + grant.subject, nonce: grant.failure === "nonce" ? "wrong-nonce" : grant.nonce,
      name: manual !== undefined && grant.subject === input.continuityOperator ? "Session continuity probe"
        : grant.subject === input.operatorA ? "Operator A" : "Operator B" }, grant.failure === "signature");
    if (manual === undefined) { issuedTokens.push({ access, id }); } else { manualTokenExchanges++; }
    response.writeHead(200, { "Content-Type": "application/json" });
    response.end(JSON.stringify({ access_token: access, id_token: id, token_type: "Bearer", expires_in: lifetime,
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
