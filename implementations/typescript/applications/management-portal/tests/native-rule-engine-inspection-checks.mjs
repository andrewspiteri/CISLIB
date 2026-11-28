import assert from "node:assert/strict";
import { expect } from "@playwright/test";
import { ruleEngineRoute } from "../src/rule-engine-routes.ts";

export async function verifyRuleEngineInspection(options) {
  let stage = "execution-list";
  try { return await inspect(options, next => { stage = next; }); }
  catch (error) { if (error instanceof Error) { error.ruleEngineStage = stage; } throw error; }
}

async function inspect({ page, pageB, origin, applicationA, applicationB, workflow, inspection, screenshot, cases }, stage) {
  const { rulesetId } = workflow;
  const portalBase = origin + "/api/portal/applications/" + applicationA + "/rule-engine";
  const target = { applicationId: applicationA, rulesetId };
  const { completedId, emptyId, failedId, cancelledId, incompleteId, instanceId, historicalVersion,
    expectedDecimal, expectedMetadata, startedFromUtc, startedThroughUtc } = inspection;
  const direct = id => origin + ruleEngineRoute({ ...target, view: "execution", executionId: id });
  const detail = page.getByRole("region", { name: "Execution detail" });
  const detailUrl = id => portalBase + "/execution?" + new URLSearchParams({ rulesetId, executionId: id });
  const countsUrl = portalBase + "/counters?" + new URLSearchParams({ rulesetId, pageSize: "100" });
  await page.getByRole("link", { name: "Executions", exact: true }).click();
  const history = page.getByRole("region", { name: "Execution history" });
  await expect(history).toBeVisible();
  await history.getByLabel("Version", { exact: true }).fill(historicalVersion);
  await history.getByLabel("Instance", { exact: true }).fill(instanceId);
  await history.getByLabel("Started from (UTC)", { exact: true }).fill(startedFromUtc);
  await history.getByLabel("Started through (UTC)", { exact: true }).fill(startedThroughUtc);
  await history.getByLabel("Outcome", { exact: true }).selectOption("Completed");
  await history.getByRole("button", { name: "Find executions", exact: true }).click();
  await expect(page).toHaveURL(next => next.searchParams.get("version") === historicalVersion && next.searchParams.get("status") === "Completed"
    && next.searchParams.get("instanceId") === instanceId);
  await expect(history.locator("#rm11-execution-" + completedId)).toBeVisible();
  const emptyRow = history.getByRole("row").filter({ has: page.locator("#rm11-execution-" + emptyId) });
  await expect(emptyRow.getByRole("cell", { name: "0", exact: true })).toBeVisible();
  await screenshot(page, "rm11-native-execution-filters");
  await history.getByLabel("Execution ID", { exact: true }).fill(completedId);
  await history.getByRole("button", { name: "Find executions", exact: true }).click();
  await expect(page).toHaveURL(next => next.searchParams.get("executionId") === completedId);
  await expect(history.getByRole("link", { name: "Open detail", exact: true })).toHaveCount(1);
  await history.getByRole("link", { name: "Open detail", exact: true }).click();

  stage("execution-detail-evidence");
  await expect(detail.getByRole("heading", { name: "Supplied inputs", exact: true })).toBeVisible();
  await expect(detail.getByText(expectedDecimal, { exact: true }).first()).toBeVisible();
  await expect(detail.getByText("Omitted", { exact: true }).first()).toBeVisible();
  await expect(detail.getByText("Supplied null", { exact: true })).toBeVisible();
  await expect(detail.getByText("Undeclared input key", { exact: true })).toBeVisible();
  await expect(detail.getByText(expectedMetadata, { exact: true }).first()).toBeVisible();
  const actualDetail = (await (await page.request.get(detailUrl(completedId))).json()).value;
  assert.equal(actualDetail.execution.returnedMatchCount, 2);
  const actualRows = JSON.parse(actualDetail.definitionJson).rows;
  const reordered = JSON.parse(actualDetail.completionAuditJson).details.selectedRowIndices.map(index => actualRows[index]);
  const selectedRows = detail.locator(".rm11-returned tbody tr");
  await expect(selectedRows).toHaveCount(reordered.length);
  for (let index = 0; index < reordered.length; index++) {
    await expect(selectedRows.nth(index).getByRole("cell").nth(0)).toHaveText(String(index + 1));
    await expect(selectedRows.nth(index).getByRole("cell").nth(2)).toHaveText(reordered[index].weight);
  }
  await expect(detail.getByText("Matched · test hit", { exact: true }).first()).toBeVisible();
  await detail.locator(".rm11-trace details summary").first().click();
  await expect(detail.getByRole("columnheader", { name: "Supplied value", exact: true })).toBeVisible();
  await screenshot(page, "rm11-native-execution-detail");
  await detail.getByRole("link", { name: "Back to executions", exact: false }).click();
  await expect(history.getByLabel("Execution ID", { exact: true })).toHaveValue(completedId);
  await expect(history.locator("#rm11-execution-" + completedId)).toBeFocused();
  await page.goto(direct(emptyId));
  await expect(detail.getByText("Completed with no returned rows.", { exact: true })).toBeVisible();
  for (const [id, outcome] of [[failedId, "Failed"], [cancelledId, "Cancelled"], [incompleteId, "Incomplete"]]) {
    await page.goto(direct(id));
    await expect(detail.getByText(outcome, { exact: true }).first()).toBeVisible();
    await expect(detail.getByText("No usable result is reported for this execution.", { exact: true })).toBeVisible();
  }
  cases.push("rm11-native-execution-filters-exact-id-focus-return-and-protected-lossless-observation-states");

  stage("single-selected-and-full-counter-resets");
  await page.getByRole("link", { name: "Counters", exact: true }).click();
  const counters = page.getByRole("region", { name: "Hit counters" });
  await expect(counters.getByRole("heading", { name: "Hit counters", exact: true })).toBeVisible();
  const countsBefore = (await (await page.request.get(countsUrl)).json()).value;
  assert.ok(countsBefore.items.some(item => item.count !== undefined && BigInt(item.count) > 0n));
  const resetPath = portalBase + "/counter-resets?" + new URLSearchParams({ rulesetId });
  const resetKeys = [];
  const resetAt = [];
  for (const selection of ["one", "selected", "all"]) {
    if (selection === "one") { await counters.getByRole("button", { name: /Reset .*eligibility-primary/ }).click(); }
    else if (selection === "selected") {
      await counters.getByRole("checkbox", { name: /Select .*eligibility-primary/ }).check();
      await counters.getByRole("checkbox", { name: /Select .*eligibility-secondary/ }).check();
      await counters.getByRole("button", { name: "Reset selected", exact: true }).click();
    } else { await counters.getByRole("button", { name: "Reset all", exact: true }).click(); }
    const dialog = page.getByRole("dialog");
    await expect(dialog).toBeVisible();
    await dialog.getByLabel("Reset reason", { exact: true }).fill("Native browser reset " + selection);
    await screenshot(page, "rm11-native-counter-reset-" + selection);
    const submitted = page.waitForResponse(response => response.url() === resetPath && response.request().method() === "POST");
    await dialog.getByRole("button", { name: selection === "one" ? "Reset counter" : selection === "selected" ? "Reset selected" : "Reset all", exact: true }).click();
    const response = await submitted;
    assert.equal(response.status(), 200);
    const command = response.request().postDataJSON();
    assert.equal(command.selection, selection);
    assert.equal(command.ruleIds.length, selection === "one" ? 1 : selection === "selected" ? 2 : 0);
    const result = await response.json();
    assert.equal(result.state, "ready"); assert.equal(result.value.disposition, "committed");
    assert.equal(result.value.counterReset.selection, selection);
    assert.equal(result.value.key, response.request().headers()["idempotency-key"]);
    resetKeys.push(result.value.key); resetAt.push(result.value.counterReset.resetAtUtc);
    const outcomes = counters.getByRole("region", { name: "Counter reset outcomes", exact: true });
    await expect(outcomes).toBeVisible();
    await expect(outcomes.getByRole("cell", { name: "Reset", exact: true }).first()).toBeVisible();
    const countsAfter = (await (await page.request.get(countsUrl)).json()).value;
    const expectedIds = selection === "all" ? countsBefore.items.map(item => item.ruleId) : command.ruleIds;
    for (const id of expectedIds) {
      const counter = countsAfter.items.find(item => item.ruleId === id);
      assert.equal(counter?.count, "0");
      assert.equal(counter?.lastResetAtUtc, result.value.counterReset.resetAtUtc);
    }
    await counters.getByRole("button", { name: "Dismiss original result", exact: true }).click();
  }
  await screenshot(page, "rm11-native-counter-results");
  cases.push("rm11-native-single-selected-all-counter-reset-commands-outcomes-and-observed-time");

  stage("counter-and-execution-grant-session");
  const blocked = await pageB.request.get(portalBase + "/execution?" + new URLSearchParams({ rulesetId, executionId: completedId }));
  assert.equal(blocked.status(), 404);
  await pageB.goto(origin + ruleEngineRoute({ applicationId: applicationB, rulesetId, view: "counters" }));
  const readOnlyCounters = pageB.getByRole("region", { name: "Hit counters" });
  await expect(readOnlyCounters.getByText("Counter counts are unavailable.", { exact: true })).toBeVisible();
  const readOnlyCounts = (await (await pageB.request.get(origin + "/api/portal/applications/" + applicationB + "/rule-engine/counters?"
    + new URLSearchParams({ rulesetId }))).json()).value;
  assert.equal(readOnlyCounts.availability, "unavailable");
  assert.ok(readOnlyCounts.items.every(item => item.count === undefined));
  await expect(readOnlyCounters.getByRole("button", { name: "Reset all", exact: true })).toHaveCount(0);
  await page.goto(direct(completedId));
  await expect(detail.getByText(expectedMetadata, { exact: true }).first()).toBeVisible();
  await page.getByRole("link", { name: "Sign out", exact: true }).click();
  await page.getByRole("button", { name: "Sign out", exact: true }).click();
  await page.goto(direct(completedId));
  await expect(page.getByText(expectedMetadata, { exact: true })).toHaveCount(0);
  await expect(page.getByText("Sign in required", { exact: true }).first()).toBeVisible();
  cases.push("rm11-native-independent-execution-grants-counter-reset-grant-and-signed-out-content-clear");
  return { completedId, emptyId, failedId, cancelledId, incompleteId, oneResetKey: resetKeys[0], selectedResetKey: resetKeys[1], allResetKey: resetKeys[2],
    resetAt, exactMetadata: expectedMetadata, exactDecimal: expectedDecimal };
}
