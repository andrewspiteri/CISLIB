// Native modal recovery within the existing headed version-page fixture.
import assert from "node:assert/strict";
import { expect } from "@playwright/test";
import { configurationVersionLabel } from "../src/configuration-versions.ts";
import { capturePortalContext, portalContextHeader, portalContextKey } from "../src/portal-context.ts";

export async function verifyDeploySessionRetryBrowser({ page, origin, applicationA, version, screenshot, cases }) {
  assert.equal(version.applicationId, applicationA);
  const sessionRead = origin + "/api/portal/applications";
  const note = "Retain this deployment note · 日本語 🌍";
  const dialog = page.locator("dialog.configuration-deploy-dialog");
  const heading = "Deploy " + configurationVersionLabel(version);
  const mutations = [], focusSessions = [];
  let otherPage, original, joinedChecks = 0, pointerRetry = false, keyboardRetry = false;
  const observe = request => {
    const url = new URL(request.url());
    if (url.origin === origin && url.pathname.startsWith("/api/portal/") && ["POST", "PUT", "PATCH", "DELETE"].includes(request.method())) {
      mutations.push(request.method() + " " + url.pathname);
    }
  };
  async function activate(target) {
    for (const session of focusSessions) { await session.send("Emulation.setFocusEmulationEnabled", { enabled: false }); }
    await target.bringToFront();
    await expect.poll(() => target.evaluate(() => document.hasFocus() && document.visibilityState === "visible"), { timeout: 5000 }).toBe(true);
  }
  async function preservedSelection() {
    await expect(dialog.getByRole("heading", { name: heading, exact: true, includeHidden: true })).toHaveText(heading);
    await expect(dialog.locator("#configuration-deploy-note")).toHaveValue(note);
  }
  page.on("request", observe);
  try {
    otherPage = await page.context().newPage();
    otherPage.on("request", observe);
    for (const target of [page, otherPage]) {
      const session = await target.context().newCDPSession(target);
      focusSessions.push(session);
      await session.send("Emulation.setFocusEmulationEnabled", { enabled: false });
    }
    await activate(otherPage);
    await otherPage.goto(origin + "/applications");
    await expect(otherPage.getByRole("heading", { name: "Applications", exact: true })).toBeVisible();
    await expect(otherPage.locator(".session-check-notice")).toHaveCount(0);
    await activate(page);
    await expect(page.locator(".session-check-notice")).toHaveCount(0);
    await page.locator(`[data-version-id="${version.releaseId}"]`).getByRole("button", { name: "Deploy", exact: true }).click();
    await expect(dialog).toBeVisible();
    await expect.poll(() => dialog.evaluate(node => node.open && node.matches(":modal"))).toBe(true);
    await dialog.getByLabel("Deployment note", { exact: true }).fill(note);

    for (const method of ["pointer", "keyboard"]) {
      await activate(otherPage);
      await expect.poll(() => page.evaluate(() => document.hasFocus()), { timeout: 5000 }).toBe(false);
      await expect(otherPage.locator(".session-check-notice")).toHaveCount(0);
      const association = await failHeldFocusCheck(page, sessionRead, async () => {
        await expect(page.getByText("Checking the signed-in session…", { exact: true })).toBeVisible();
        await expect(page.locator(".protected-document")).toHaveAttribute("inert", "");
        await expectSuspended(dialog);
        await preservedSelection();
      }, () => activate(page));
      joinedChecks++;
      const retry = page.getByRole("button", { name: "Retry session check", exact: true });
      await expect(retry).toBeVisible();
      await expectSuspended(dialog);
      await preservedSelection();
      await screenshot(page, `deploy-dialog-${method}-retry-available-native`);
      await retryCurrentSession(page, sessionRead, association, async () => {
        if (method === "pointer") { await retry.click(); }
        else {
          let focused = false;
          for (let tabs = 0; tabs < 8; tabs++) {
            await page.keyboard.press("Tab");
            if (await retry.evaluate(node => document.activeElement === node)) { focused = true; break; }
          }
          assert.ok(focused, "Native Tab navigation must reach Retry outside the suspended modal.");
          await page.keyboard.press("Enter");
        }
      });
      await expect(page.locator(".session-check-notice")).toHaveCount(0);
      await expect(dialog).toBeVisible();
      await expect.poll(() => dialog.evaluate(node => node.open && node.matches(":modal"))).toBe(true);
      await preservedSelection();
      assert.deepEqual(mutations, [], "Session retry and modal restoration must not preview or deploy Configuration.");
      if (method === "pointer") { pointerRetry = true; } else { keyboardRetry = true; }
      await screenshot(page, `deploy-dialog-${method}-retry-restored-native`);
    }
    await dialog.getByRole("button", { name: "Cancel", exact: true }).click();
    await expectSuspended(dialog);
    await expect(dialog.locator("#configuration-deploy-note")).toHaveCount(0);
    assert.deepEqual(mutations, []);
    cases.push("native-deploy-modal-suspends-during-held-and-unavailable-focus-checks-without-losing-selected-version-or-note",
      "native-pointer-and-tab-enter-session-retries-restore-same-session-deploy-dialog-with-no-preview-or-deployment");
  } catch (error) { original = error; }
  finally {
    page.off("request", observe);
    otherPage?.off("request", observe);
    for (const session of focusSessions.reverse()) {
      try { await session.send("Emulation.setFocusEmulationEnabled", { enabled: true }); }
      catch (error) { original = combine(original, error); }
      finally { try { await session.detach(); } catch (error) { original = combine(original, error); } }
    }
    if (otherPage !== undefined) {
      try { await otherPage.close(); } catch (error) { original = combine(original, error); }
    }
  }
  if (original !== undefined) { throw original; }
  return { releaseId: version.releaseId, mutations: mutations.length, heldChecksJoined: joinedChecks, pointerRetry, keyboardRetry,
    modalSuspendedWhileChecking: true, modalSuspendedWhileUnavailable: true, sameSelectionAndNoteRestored: true,
    cancelRestored: true, nativeOwnersJoined: true };
}

async function expectSuspended(dialog) {
  await expect.poll(() => dialog.evaluate(node => ({ open: node.open, modal: node.matches(":modal") }))).toEqual({ open: false, modal: false });
  await expect(dialog).not.toBeVisible();
}

async function retryCurrentSession(page, url, association, activate) {
  const response = page.waitForResponse(value => value.url() === url && value.request().method() === "GET", { timeout: 20000 }).then(async value => {
    assert.equal(value.status(), 200);
    const reply = await value.json(), context = capturePortalContext(reply.context);
    assert.equal(reply.state, "ready"); assert.ok(context);
    assert.equal(portalContextKey(context), association, "Only authoritative confirmation of the same session may restore the dialog.");
  });
  const outcomes = await Promise.allSettled([response, activate()]);
  let original;
  for (const outcome of outcomes) { if (outcome.status === "rejected") { original = combine(original, outcome.reason); } }
  if (original !== undefined) { throw original; }
}

async function failHeldFocusCheck(page, url, inspect, activate) {
  let entered = false, ready = false, release, finish, failure, association;
  const released = new Promise(resolve => { release = resolve; });
  const joined = new Promise(resolve => { finish = resolve; });
  const handler = async route => {
    if (entered || route.request().method() !== "GET") {
      try { await route.continue(); } catch (error) { failure = combine(failure, error); }
      return;
    }
    entered = true;
    let response;
    try {
      response = await route.fetch({ timeout: 15000 });
      assert.equal(response.status(), 200);
      const reply = await response.json(), context = capturePortalContext(reply.context);
      assert.equal(reply.state, "ready"); assert.ok(context);
      association = portalContextKey(context);
      assert.equal(association, route.request().headers()[portalContextHeader]);
      ready = true;
      await released;
      await route.fulfill({ status: 503, json: { state: "unavailable", message: "Controlled unavailable session delivery", context: reply.context } });
    } catch (error) {
      failure = combine(failure, error); ready = true;
      try { await route.abort("failed"); } catch (cleanup) { failure = combine(failure, cleanup); }
    } finally {
      try { if (response !== undefined) { await response.dispose(); } }
      catch (error) { failure = combine(failure, error); }
      finally { finish(); }
    }
  };
  await page.route(url, handler);
  let original;
  try {
    await activate();
    await expect.poll(() => ready, { timeout: 10000 }).toBe(true);
    if (failure !== undefined) { throw failure; }
    await inspect();
  } catch (error) { original = error; }
  finally {
    release();
    try { if (entered) { await joined; } }
    catch (error) { original = combine(original, error); }
    finally { try { await page.unroute(url, handler); } catch (error) { original = combine(original, error); } }
  }
  if (failure !== undefined && failure !== original) { original = combine(original, failure); }
  if (original !== undefined) { throw original; }
  assert.equal(typeof association, "string");
  return association;
}

function combine(first, next) { return first === undefined ? next : first === next ? first : new AggregateError([first, next], "The native Deploy session check and owned cleanup both failed."); }
