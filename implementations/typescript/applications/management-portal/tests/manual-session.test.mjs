import assert from "node:assert/strict";
import { mkdtempSync, realpathSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { basename, dirname, join } from "node:path";
import test from "node:test";
import { captureManualSession, fixtureTokenLifetimeSeconds, localLoginPage, waitForManualContinuityProbe, waitForManualStop } from "./manual-session.mjs";

test("manual mode is absent for ordinary fixtures and refuses unbounded leases", () => {
  assert.equal(captureManualSession(null), undefined);
  assert.equal(captureManualSession(undefined), undefined);
  const directory = mkdtempSync(join(tmpdir(), "cis-portal-manual-controls-"));
  try {
    const input = { controlDirectory: directory, leaseSeconds: 3600, openBrowser: true };
    assert.equal(captureManualSession(input).leaseSeconds, 3600);
    for (const leaseSeconds of [0, 29, 3601, Infinity, 30.5]) {
      assert.throws(() => captureManualSession({ ...input, leaseSeconds }));
    }
    assert.throws(() => captureManualSession({ ...input, controlDirectory: "relative" }));
    assert.throws(() => captureManualSession({ ...input, unexpected: true }));
  } finally { removeOwnedDirectory(directory, "cis-portal-manual-controls-"); }
});

test("local chooser retains callback checks and escapes every copied field", () => {
  const url = new URL("https://localhost:1234/tenant/oauth2/v2.0/authorize");
  for (const [name, value] of Object.entries({ state: 'state<&"', nonce: "nonce", code_challenge: "challenge", code_challenge_method: "S256",
    redirect_uri: "https://127.0.0.1:5678/api/auth/callback/cis-management", fixture_failure: "signature" })) { url.searchParams.set(name, value); }
  const page = localLoginPage(url, "operator-a", "operator-b");
  assert.ok(page.includes('name="state" value="state&lt;&amp;&quot;"'));
  assert.ok(page.includes('name="nonce" value="nonce"') && page.includes('name="code_challenge" value="challenge"'));
  assert.ok(page.includes('name="code_challenge_method" value="S256"') && page.includes('name="redirect_uri"'));
  assert.equal(page.match(/name="fixture_subject"/gu).length, 2);
  assert.ok(page.includes("Operator A — Country Services and Billing Worker editor"));
  assert.ok(page.includes("Operator B — Billing Worker reader"));
  assert.ok(!page.includes("fixture_failure"));
});

test("manual tokens are minted at redemption within the captured lease rather than the automatic test lifetime", () => {
  const manual = { leaseSeconds: 3600 };
  const started = 1_800_000_000_000;
  const end = started + 3_600_000;
  assert.equal(fixtureTokenLifetimeSeconds(undefined, undefined, started), 600);
  assert.equal(fixtureTokenLifetimeSeconds(manual, end, started), 3600);
  assert.equal(fixtureTokenLifetimeSeconds(manual, end, started + 120_000), 3480);
  assert.equal(fixtureTokenLifetimeSeconds(manual, end, started + 900_000), 2700);
  assert.equal(fixtureTokenLifetimeSeconds(manual, end, end - 30_000), 30);
  assert.equal(fixtureTokenLifetimeSeconds(manual, end, end), 0);
  assert.equal(fixtureTokenLifetimeSeconds(manual, end, end + 1_000), 0);
  assert.throws(() => fixtureTokenLifetimeSeconds(manual, end, started - 1_000));
  assert.throws(() => fixtureTokenLifetimeSeconds(manual, NaN, started));
  assert.throws(() => fixtureTokenLifetimeSeconds({ leaseSeconds: 3601 }, end, started));
});

test("manual continuity cancellation settles the one owned timer without claiming the elapsed proof", async () => {
  const stop = new AbortController();
  const pending = waitForManualContinuityProbe(stop.signal);
  stop.abort();
  assert.equal(await pending, false);
  assert.equal(await waitForManualContinuityProbe(stop.signal), false);
});

test("explicit stop wins without waiting for the finite lease", async () => {
  const directory = mkdtempSync(join(tmpdir(), "cis-portal-manual-stop-"));
  try {
    writeFileSync(join(directory, "stop.request"), "stop");
    assert.equal(await waitForManualStop({ controlDirectory: directory, leaseSeconds: 30, hostExit: new Promise(() => {}) }), "requested");
  } finally { removeOwnedDirectory(directory, "cis-portal-manual-stop-"); }
});

test("unexpected host completion rejects and retires the stop timer", async () => {
  const directory = mkdtempSync(join(tmpdir(), "cis-portal-manual-exit-"));
  try {
    await assert.rejects(waitForManualStop({ controlDirectory: directory, leaseSeconds: 30, hostExit: Promise.resolve({ code: 0 }) }),
      /host exited before/u);
  } finally { removeOwnedDirectory(directory, "cis-portal-manual-exit-"); }
});

test("a failed continuity owner interrupts the lease and retains its original error", async () => {
  const directory = mkdtempSync(join(tmpdir(), "cis-portal-manual-probe-failure-"));
  const failure = new AbortController();
  const original = new Error("owned continuity status write failed");
  try {
    const stopped = waitForManualStop({ controlDirectory: directory, leaseSeconds: 30, hostExit: new Promise(() => {}), failureSignal: failure.signal });
    failure.abort(original);
    await assert.rejects(stopped, error => error === original);
    await assert.rejects(waitForManualStop({ controlDirectory: directory, leaseSeconds: 30,
      hostExit: new Promise(() => {}), failureSignal: failure.signal }), error => error === original);
  } finally { removeOwnedDirectory(directory, "cis-portal-manual-probe-failure-"); }
});

function removeOwnedDirectory(directory, expectedPrefix) {
  const resolved = realpathSync(directory);
  assert.equal(dirname(resolved), realpathSync(tmpdir()), "Cleanup must stay inside the actual temporary directory.");
  assert.ok(basename(resolved).startsWith(expectedPrefix), "Cleanup must retain its generated fixture prefix.");
  rmSync(resolved, { recursive: true });
}
