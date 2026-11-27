// Real independent native sessions, two shared-cookie tabs, and the ordinary production provider.
import assert from "node:assert/strict";
import { expect } from "@playwright/test";
import { applicationRoute, configurationRoute } from "../src/portal-routes.ts";

const configurationId = "shared.features";
const draft = '{"TabReview":"Private unsaved draft must retire with its actual session","Enabled":false}';
const note = "Memory-only cross-tab draft";

export async function verifySessionTabsBrowser({ page, secondPage, origin, applicationA, operatorBContext,
  renewedOperatorAContext, screenshot, cases }) {
  const configuration = origin + configurationRoute({ applicationId: applicationA, applicationName: "managed-a", configurationId, schemaVersion: "1" });
  const list = origin + applicationRoute({ applicationId: applicationA, applicationName: "managed-a", tab: "instances" });
  const sessionRead = origin + "/api/portal/applications";
  const mutations = [];
  const focusSessions = new Map();
  const activations = [];
  const checks = { first: 0, second: 0 };
  const tab = value => value === page ? "first" : "second";
  const observe = request => {
    const url = new URL(request.url());
    if (url.href === sessionRead && request.method() === "GET") { checks[tab(request.frame().page())]++; }
    if (url.origin === origin && url.pathname.startsWith("/api/portal/") && ["POST", "PUT", "PATCH", "DELETE"].includes(request.method())) {
      mutations.push(request.method() + " " + url.pathname);
    }
  };
  page.on("request", observe); secondPage.on("request", observe);
  let original, proof;
  async function activate(target) {
    // Pinned Playwright normally emulates every page as focused. Remove that
    // override so bringToFront exercises the real headed tab/window events.
    for (const session of focusSessions.values()) { await session.send("Emulation.setFocusEmulationEnabled", { enabled: false }); }
    await target.bringToFront();
    await expect.poll(() => target.evaluate(() => document.hasFocus() && document.visibilityState === "visible"), { timeout: 5000 }).toBe(true);
    const state = await target.evaluate(() => ({ focused: document.hasFocus(), visibility: document.visibilityState,
      masked: document.querySelector(".protected-document")?.getAttribute("data-session-masked") === "true" }));
    assert.ok(activations.length < 32, "The named two-document scenario has a finite activation bound.");
    activations.push({ tab: tab(target), ...state, checks: checks[tab(target)] });
  }
  try {
    for (const target of [page, secondPage]) {
      const session = await target.context().newCDPSession(target);
      focusSessions.set(target, session);
      await session.send("Emulation.setFocusEmulationEnabled", { enabled: false });
    }
    await activate(page);
    await page.goto(configuration);
    await expect(page.locator(".configuration-json-readonly")).toBeVisible();
    await editDraft(page);
    await activate(secondPage);
    await secondPage.goto(list);
    await secondPage.getByRole("button", { name: "seed-a", exact: true }).click();
    const panel = secondPage.getByRole("dialog");
    await expect(panel.getByRole("heading", { name: "seed-a", exact: true })).toBeVisible();
    await expect(panel).toContainText("1.0.0");
    await heldActualCheck(secondPage, page, sessionRead, async () => {
      await expect(panel).not.toBeVisible();
      await expect(secondPage.locator(".protected-document")).toHaveAttribute("inert", "");
    }, {}, activate);
    await expect(panel).toBeVisible();
    await panel.getByRole("button", { name: "Close details", exact: true }).click();
    await heldActualCheck(page, secondPage, sessionRead, async () => {
      await expect(page.locator("#configuration-version-json")).not.toBeVisible();
      assert.equal(await page.locator("#configuration-version-json").inputValue(), draft);
      await expect(page.getByText("Checking the signed-in session…", { exact: true })).toBeVisible();
    }, {}, activate);
    await expect(page.locator("#configuration-version-json")).toBeVisible();
    await expect(page.locator("#configuration-version-json")).toHaveValue(draft);
    await expect(page.getByLabel("Version note", { exact: true })).toHaveValue(note);
    cases.push("native-two-tabs-real-focus-check-masks-inline-and-portaled-private-content-without-discarding-a-valid-draft");

    await heldActualCheck(page, secondPage, sessionRead, async () => {
      await expect(page.locator("#configuration-version-json")).not.toBeVisible();
    }, { unavailable: true }, activate);
    await expect(page.getByText("The signed-in session could not be verified. Protected content remains hidden.", { exact: true })).toBeVisible();
    await expect(page.locator("#configuration-version-json")).not.toBeVisible();
    assert.equal(await page.locator("#configuration-version-json").inputValue(), draft);
    await page.getByRole("button", { name: "Retry session check", exact: true }).click();
    await expect(page.locator("#configuration-version-json")).toBeVisible();
    await expect(page.locator("#configuration-version-json")).toHaveValue(draft);
    cases.push("native-unverifiable-focus-delivery-stays-masked-and-explicit-current-session-retry-restores-the-same-draft");

    // Only this independently signed-in browser identity is revoked. The host's API A stays valid.
    await heldActualCheck(page, secondPage, sessionRead, async () => {
      await activate(secondPage);
      await secondPage.getByRole("link", { name: "Sign out", exact: true }).click();
      await expect(secondPage.getByRole("button", { name: "Sign out", exact: true })).toBeVisible();
      await secondPage.getByRole("button", { name: "Sign out", exact: true }).click();
      await expect(page.locator("#configuration-version-json")).toHaveCount(0);
      await expect(page.getByText("Sign in required", { exact: true }).first()).toBeVisible();
    }, { retired: true }, activate);
    await expect(page.locator("#configuration-version-json")).toHaveCount(0);
    assert.equal(await page.locator("body").textContent().then(text => text.includes("Private unsaved draft")), false);
    await screenshot(page, "session-tabs-peer-signout-cleared-draft-native");
    cases.push("native-actual-signout-in-a-shared-cookie-tab-clears-peer-private-json-and-draft-before-a-held-old-context-can-return");

    await replaceCookies(page.context(), renewedOperatorAContext);
    await activate(page);
    await page.goto(configuration);
    await expect(page.locator(".configuration-json-readonly")).toBeVisible();
    await editDraft(page);
    await activate(secondPage);
    await secondPage.goto(list);
    await expect(secondPage.getByRole("button", { name: "seed-a", exact: true })).toBeVisible();
    await expect(secondPage.locator(".session-check-notice")).toHaveCount(0);
    await replaceCookies(page.context(), operatorBContext);
    const response = page.waitForResponse(value => value.url() === sessionRead && value.request().method() === "GET")
      .then(async value => ({ status: value.status(), body: await value.json() }));
    const outcomes = await Promise.allSettled([response, activate(page)]);
    for (const outcome of outcomes) { if (outcome.status === "rejected") { throw outcome.reason; } }
    assert.equal(outcomes[0].value.status, 409);
    assert.equal(outcomes[0].value.body.state, "invalid");
    assert.equal("value" in outcomes[0].value.body, false);
    await expect(page.locator("#configuration-version-json")).toHaveCount(0);
    await expect(page.getByText("Sign in required", { exact: true }).first()).toBeVisible();
    assert.equal(await page.locator("body").textContent().then(text => text.includes("Private unsaved draft")), false);
    await screenshot(page, "session-tabs-real-operator-switch-cleared-draft-native");
    assert.deepEqual(mutations, []);
    cases.push("native-missed-notification-real-cookie-operator-switch-is-detected-by-authoritative-focus-409-with-no-configuration-effects");
    proof = { mutations: mutations.length, sameSessionDraftPreserved: true, pendingProtectedSurfacesHidden: true,
      unavailableStaysMaskedWithDraft: true, signoutClearedPeer: true, operatorSwitchClearedPeer: true, heldContextReadJoined: true };
  } catch (error) {
    const states = [];
    for (const target of [page, secondPage]) {
      try { states.push({ tab: tab(target), ...await target.evaluate(() => {
        const wrapper = document.querySelector(".protected-document"), panel = document.querySelector('[role="dialog"]');
        const inert = node => {
          const value = node?.getAttribute("inert");
          return value === null || value === undefined ? null : ["", "true", "false"].includes(value) ? value : "other";
        };
        const display = node => {
          if (node === null) { return null; }
          const value = getComputedStyle(node).display;
          return ["none", "contents", "block", "flex", "grid"].includes(value) ? value : "other";
        };
        return { focused: document.hasFocus(), visibility: document.visibilityState,
          masked: wrapper?.getAttribute("data-session-masked") === "true", wrapperInertPresent: wrapper?.hasAttribute("inert") === true,
          wrapperInertValue: inert(wrapper), wrapperDisplay: display(wrapper), panelPresent: panel !== null,
          panelMasked: panel?.getAttribute("data-session-masked") === "true", panelInertPresent: panel?.hasAttribute("inert") === true,
          panelInertValue: inert(panel), panelDisplay: display(panel), activeElementInsidePanel: panel?.contains(document.activeElement) === true,
          unavailable: [...document.querySelectorAll(".session-check-notice")].some(node => node.textContent?.includes("could not be verified")) };
      }) }); }
      catch { states.push({ tab: tab(target), stateUnavailable: true }); }
    }
    console.error("[PortalSessionTabs] " + JSON.stringify({ checks, activations, states, errors: errorLocations(error) }));
    original = error;
  } finally {
    page.off("request", observe); secondPage.off("request", observe);
    for (const session of [...focusSessions.values()].reverse()) {
      try { await session.send("Emulation.setFocusEmulationEnabled", { enabled: true }); }
      catch (error) { original = combine(original, error); }
      finally { try { await session.detach(); } catch (error) { original = combine(original, error); } }
    }
  }
  if (original !== undefined) { throw original; }
  return proof;
}

async function editDraft(page) {
  await page.getByRole("button", { name: "Edit as new version", exact: true }).click();
  await page.locator("#configuration-version-json").fill(draft);
  await page.getByLabel("Version note", { exact: true }).fill(note);
  await expect(page.getByRole("button", { name: "Save version", exact: true })).toBeEnabled();
}

async function replaceCookies(context, api) {
  await context.clearCookies();
  await context.addCookies((await api.storageState()).cookies);
}

async function heldActualCheck(page, otherPage, url, inspect, { unavailable = false, retired = false } = {}, activate) {
  await activate(otherPage);
  await expect.poll(() => page.evaluate(() => document.hasFocus()), { timeout: 5000 }).toBe(false);
  await expect(otherPage.locator(".session-check-notice")).toHaveCount(0);
  let entered = false, ready = false, finish, release, failure;
  const joined = new Promise(resolve => { finish = resolve; });
  const released = new Promise(resolve => { release = resolve; });
  const handler = async route => {
    if (entered || route.request().method() !== "GET") {
      try { await route.continue(); } catch (error) { failure ??= error; }
      return;
    }
    entered = true;
    let response;
    try {
      response = await route.fetch({ timeout: 15000 });
      assert.equal(response.status(), 200);
      const actual = await response.json();
      assert.equal(actual.state, "ready"); assert.ok(actual.context);
      ready = true;
      await released;
      try {
        if (unavailable) { await route.fulfill({ status: 503, json: { state: "unavailable", message: "Controlled unavailable delivery", context: actual.context } }); }
        else { await route.fulfill({ response }); }
      } catch (error) {
        if (!retired || route.request().failure()?.errorText !== "net::ERR_ABORTED") { throw error; }
      }
    } catch (error) {
      failure = combine(failure, error); ready = true;
      try { await route.abort("failed"); }
      catch (cleanup) { failure = combine(failure, cleanup); }
    } finally {
      try { if (response !== undefined) { await response.dispose(); } }
      catch (error) { failure = combine(failure, error); }
      finally { finish(); }
    }
  };
  await page.route(url, handler);
  let original;
  try {
    await activate(page);
    await expect.poll(() => ready, { timeout: 10000 }).toBe(true);
    if (failure !== undefined) { throw failure; }
    await inspect();
  } catch (error) { original = error; }
  finally {
    release();
    // Keep the interception installed until its owned response has actually
    // fulfilled/disposed. Unrouting first can invalidate that response route.
    try { if (entered) { await joined; } }
    catch (error) { original = combine(original, error); }
    finally {
      try { await page.unroute(url, handler); }
      catch (error) { original = combine(original, error); }
    }
  }
  if (failure !== undefined && failure !== original) { original = combine(original, failure); }
  if (original !== undefined) { throw original; }
}

function combine(first, next) { return first === undefined ? next : first === next ? first : new AggregateError([first, next], "The focus-check assertion and owned response cleanup both failed."); }

function errorLocations(error) {
  const result = [];
  function visit(value, depth) {
    if (result.length >= 12) { return; }
    const name = value instanceof Error ? value.name : "Unknown";
    const match = value instanceof Error ? /\b(native-session-tabs-checks\.mjs):(\d{1,6}):(\d{1,6})/u.exec(value.stack ?? "") : null;
    result.push({ kind: /^[A-Za-z][A-Za-z0-9]{0,63}$/u.test(name) ? name : "Error", depth,
      ...(match === null ? {} : { sourceFile: match[1], sourceLine: Number(match[2]), sourceColumn: Number(match[3]) }) });
    if (value instanceof AggregateError && depth < 3) { for (const child of value.errors.slice(0, 4)) { visit(child, depth + 1); } }
  }
  visit(error, 0);
  return result;
}
