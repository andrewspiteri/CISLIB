// Small, test-only controls for the existing native Management portal fixture.
import assert from "node:assert/strict";
import { existsSync, lstatSync } from "node:fs";
import { isAbsolute, join, resolve } from "node:path";

export function captureManualSession(value) {
  if (value === undefined || value === null) { return undefined; }
  assert.ok(typeof value === "object" && !Array.isArray(value));
  assert.ok(Object.keys(value).every(key => ["controlDirectory", "leaseSeconds", "openBrowser"].includes(key)));
  assert.ok(typeof value.controlDirectory === "string" && isAbsolute(value.controlDirectory));
  const controlDirectory = resolve(value.controlDirectory);
  const directory = lstatSync(controlDirectory);
  assert.ok(directory.isDirectory() && !directory.isSymbolicLink(), "The owner supplies an existing ordinary control directory.");
  assert.ok(Number.isInteger(value.leaseSeconds) && value.leaseSeconds >= 30 && value.leaseSeconds <= 3600,
    "The manual fixture lease must be 30 through 3600 seconds.");
  assert.ok(value.openBrowser === undefined || typeof value.openBrowser === "boolean");
  return Object.freeze({ controlDirectory, leaseSeconds: value.leaseSeconds, openBrowser: value.openBrowser === true });
}

/** The synthetic issuer stays inside the explicit review lease; ordinary automated tokens retain their ten-minute lifetime. */
export function fixtureTokenLifetimeSeconds(manual, leaseExpiresAtMs, nowMs = Date.now()) {
  if (manual === undefined) { return 600; }
  assert.ok(Number.isInteger(manual.leaseSeconds) && manual.leaseSeconds >= 30 && manual.leaseSeconds <= 3600);
  assert.ok(Number.isSafeInteger(nowMs) && nowMs >= 0 && Number.isSafeInteger(leaseExpiresAtMs)
    && leaseExpiresAtMs >= 0 && leaseExpiresAtMs <= 8640000000000000);
  const remaining = Math.max(0, Math.floor(leaseExpiresAtMs / 1000) - Math.floor(nowMs / 1000));
  assert.ok(remaining <= manual.leaseSeconds, "A changed clock cannot extend the captured review lease.");
  return remaining;
}

/** One named continuity observation; stopping the fixture retires and joins this timer. */
export function waitForManualContinuityProbe(signal) {
  if (signal.aborted) { return Promise.resolve(false); }
  return new Promise(resolveWait => {
    const finish = completed => {
      clearTimeout(timer);
      signal.removeEventListener("abort", cancel);
      resolveWait(completed);
    };
    const cancel = () => finish(false);
    const timer = setTimeout(() => finish(true), 130000);
    signal.addEventListener("abort", cancel, { once: true });
    if (signal.aborted) { cancel(); }
  });
}

export function localLoginPage(url, operatorA, operatorB) {
  const fields = [...url.searchParams]
    .filter(([name]) => name !== "fixture_subject" && name !== "fixture_failure")
    .map(([name, value]) => `<input type="hidden" name="${html(name)}" value="${html(value)}">`).join("");
  return `<!doctype html><html lang="en"><meta charset="utf-8"><meta name="viewport" content="width=device-width">
<title>Local test sign-in</title><body><main><h1>Local test sign-in</h1>
<p>This isolated fixture uses synthetic operators and sample data. Choose an operator to continue through the portal's ordinary OIDC callback.</p>
<form method="get" action="${html(url.pathname)}">${fields}
<p><button type="submit" name="fixture_subject" value="${html(operatorA)}">Operator A — Country Services and Billing Worker editor</button></p>
<p><button type="submit" name="fixture_subject" value="${html(operatorB)}">Operator B — Billing Worker reader</button></p>
</form><p>Sign-in lasts within this local review session, up to one hour. Sign out to switch operators. The review closes when its stated lease ends.</p>
</main></body></html>`;
}

export async function waitForManualStop({ controlDirectory, leaseSeconds, hostExit, failureSignal }) {
  return await new Promise((resolveStop, rejectStop) => {
    let settled = false;
    let poll;
    let deadline;
    const signalStop = () => finish("signal");
    const failedOwner = () => finish(undefined, failureSignal.reason instanceof Error
      ? failureSignal.reason : new Error("The manual continuity owner failed."));
    function finish(reason, error) {
      if (settled) { return; }
      settled = true;
      clearInterval(poll);
      clearTimeout(deadline);
      process.removeListener("SIGINT", signalStop);
      process.removeListener("SIGTERM", signalStop);
      failureSignal?.removeEventListener("abort", failedOwner);
      if (error !== undefined) { rejectStop(error); } else { resolveStop(reason); }
    }
    function check() {
      try {
        if (existsSync(join(controlDirectory, "stop.request"))) { finish("requested"); }
      } catch (error) { finish(undefined, error); }
    }
    process.once("SIGINT", signalStop);
    process.once("SIGTERM", signalStop);
    poll = setInterval(check, 250);
    // Node's finite timer keeps the lease bounded independently of wall-clock changes.
    deadline = setTimeout(() => finish("expired"), leaseSeconds * 1000);
    void hostExit.then(() => finish(undefined, new Error("The portal host exited before the manual session stopped.")),
      () => finish(undefined, new Error("The portal host failed during the manual session.")));
    failureSignal?.addEventListener("abort", failedOwner, { once: true });
    if (failureSignal?.aborted) { failedOwner(); } else { check(); }
  });
}

function html(value) {
  return String(value).replace(/[&<>"']/gu, character => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[character]);
}
