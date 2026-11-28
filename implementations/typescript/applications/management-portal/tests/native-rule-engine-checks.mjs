import assert from "node:assert/strict";
import { randomUUID } from "node:crypto";
import { expect } from "@playwright/test";
import { portalContextHeader, portalContextKey } from "../src/portal-context.ts";
import { ruleEngineRoute } from "../src/rule-engine-routes.ts";

export async function verifyRuleEngineWorkflow(options) {
  let stage = "native-seed-reads";
  try {
    return await runWorkflow(options, next => { stage = next; });
  } catch (error) {
    if (error instanceof Error) { error.ruleEngineStage = stage; }
    throw error;
  }
}

export async function verifyRuleEngineSchemaUpdate({ page, origin, applicationA, schemaUpdate, screenshot, cases }) {
  const { rulesetId, previousVersion, previousSchemaRevision, currentSchemaRevision, addedColumnName, addedColumnId } = schemaUpdate;
  const native = origin + "/api/portal/applications/" + applicationA + "/rule-engine";
  const url = (route, selectors = {}) => native + "/" + route + "?" + new URLSearchParams({ rulesetId, ...selectors });
  const previous = (await read(page, url("version", { version: previousVersion }))).value;
  const assignment = (await read(page, url("deployment"))).value;
  const schema = (await read(page, url("schema", { schemaRevision: currentSchemaRevision }))).value;
  const capabilities = (await read(page, native + "/capabilities")).value;
  assert.ok(capabilities.some(item => item.operationId === "getRuleEngineSchema" && item.allowed));
  const columns = JSON.parse(schema.schemaJson).columns;
  assert.equal(previous.schemaRevision, previousSchemaRevision);
  assert.equal(schema.schemaRevision, currentSchemaRevision);
  assert.ok(columns.some(column => column.id === addedColumnId && column.displayName === addedColumnName));

  await page.goto(origin + ruleEngineRoute({ applicationId: applicationA, rulesetId }));
  await expect(page.getByText("This saved version uses an earlier schema. Its definition remains read-only.")).toBeVisible();
  const start = page.getByRole("link", { name: "Start version with current schema", exact: true });
  await expect(start).toBeVisible();
  await expect(page.getByRole("button", { name: "Add rule", exact: true })).toHaveCount(0);
  await page.getByRole("link", { name: "Versions", exact: true }).click();
  await expect(page.getByText("Read only", { exact: false })).toBeVisible();
  await expect(page.getByRole("link", { name: "Edit as new version", exact: true })).toHaveCount(0);
  await start.click();
  await expect(page).toHaveURL(next => next.pathname.endsWith("/new-version") && next.searchParams.get("schemaRevision") === currentSchemaRevision
    && !next.searchParams.has("version"));

  const grid = page.getByRole("region", { name: "Rules grid" });
  const condition = page.getByRole("region", { name: "Condition inspector" });
  const save = page.getByRole("button", { name: "Save version", exact: true });
  await expect(grid.getByRole("columnheader", { name: addedColumnName, exact: true })).toBeVisible();
  await expect(grid.getByText("No rules", { exact: true })).toBeVisible();
  await grid.getByRole("button", { name: "Add rule", exact: true }).click();
  for (const column of columns.filter(column => column.id !== addedColumnId)) {
    await selectCell(page, 10, column.displayName);
    await condition.getByLabel("Condition", { exact: true }).selectOption("any");
  }
  const note = "Current schema version created from the portal";
  await page.getByLabel("Version note", { exact: true }).fill(note);
  await save.click();
  const addedCell = grid.getByRole("button", { name: new RegExp("^Rule 10 " + addedColumnName + ":") });
  await expect(addedCell).toHaveAttribute("aria-invalid", "true");
  await expect(condition.getByLabel("Condition", { exact: true })).toHaveValue("");
  await condition.getByLabel("Condition", { exact: true }).selectOption("boolean");
  await condition.getByLabel("Operator", { exact: true }).selectOption("equals");
  await condition.getByLabel("Boolean value", { exact: true }).selectOption("true");
  await screenshot(page, "rm11-new-schema-draft");

  const saveUrl = url("versions");
  const submitted = page.waitForResponse(response => response.url() === saveUrl && response.request().method() === "POST");
  await save.click();
  const response = await submitted;
  assert.equal(response.status(), 200);
  const command = response.request().postDataJSON();
  assert.equal(command.schemaRevision, currentSchemaRevision);
  assert.equal(Object.hasOwn(command, "baseVersion"), false);
  const decided = await response.json();
  assert.equal(decided.state, "ready");
  assert.equal(decided.value.disposition, "committed");
  const savedVersion = decided.value.version;
  assert.notEqual(savedVersion, previousVersion);
  await expect(page).toHaveURL(next => next.pathname.endsWith("/versions") && next.searchParams.get("version") === savedVersion);
  await expect(page.getByRole("heading", { name: note, exact: true })).toBeVisible();
  const saved = (await read(page, url("version", { version: savedVersion }))).value;
  const definition = JSON.parse(saved.definitionJson);
  assert.equal(definition.schemaRevision, currentSchemaRevision);
  assert.equal(definition.rows.length, 1);
  assert.deepEqual(definition.rows[0].conditions.find(cell => cell.columnId === addedColumnId),
    { columnId: addedColumnId, presetKind: "boolean", operator: "equals", preset: { kind: "boolean", value: true } });
  assert.deepEqual((await read(page, url("version", { version: previousVersion }))).value, previous);
  assert.deepEqual((await read(page, url("deployment"))).value, assignment);
  cases.push("rm11-native-schema-update-start-current-draft-save-explicit-cells-and-retain-history");
  return { savedVersion, previousVersion, schemaRevision: currentSchemaRevision };
}

async function runWorkflow({ page, pageB, origin, applicationA, applicationB, workflow, screenshot, cases }, markStage) {
  const { rulesetId, displayName, firstVersion, secondVersion, readOnlyVersion } = workflow;
  const native = origin + "/api/portal/applications/" + applicationA + "/rule-engine";
  const url = (route, except = {}) => native + "/" + route + "?" + new URLSearchParams({ rulesetId, ...except });
  const exactDecimal = "9007199254740993.000000000001", metadata = '{"balance":9007199254740993,"rate":1.2300e5}';
  const saveNote = "Portal reviewed an earlier immutable version";
  const before = (await read(page, url("deployment"))).value;
  const original = (await read(page, url("version", { version: firstVersion }))).value;
  const second = (await read(page, url("version", { version: secondVersion }))).value;
  assert.equal(before.desired.version, firstVersion);
  assert.ok(before.requestedAtUtc);
  assert.equal(original.versionNote, "Initial eligibility");
  assert.equal(second.versionNote, "Expanded trial");

  markStage("capabilities-entry");
  const capabilities = (await read(page, native + "/capabilities")).value;
  assert.ok(capabilities.some(item => item.operationId === "listRuleEngineRulesets" && item.allowed),
    "The current operator has the exact installed Rule Engine listing capability.");
  await page.goto(origin + "/applications/" + applicationA + "/capabilities");
  await page.getByRole("link", { name: "Rule engines", exact: true }).click();
  await expect(page.getByRole("heading", { name: "Registered rulesets" })).toBeVisible();
  await page.getByRole("link", { name: displayName, exact: true }).click();
  markStage("native-initial-adoption");
  await page.getByRole("link", { name: "Deployment", exact: true }).click();
  const rejected = page.getByRole("row").filter({ has: page.getByText("Rejected", { exact: true }) });
  await expect(rejected).toHaveCount(1);
  await expect(page.getByText("Applied", { exact: true })).toBeVisible();
  await expect(page.getByText("Pending", { exact: true })).toBeVisible();
  await screenshot(page, "rm11-native-adoption");
  cases.push("rm11-registered-ruleset-navigation-and-native-applied-pending-rejected-reports");

  markStage("older-version-editor");
  await page.getByRole("link", { name: "Versions", exact: true }).click();
  await page.getByRole("button", { name: "Initial eligibility", exact: true }).click();
  await expect(page.getByText("Read only", { exact: false })).toBeVisible();
  await screenshot(page, "rm11-native-read-only-version");
  await page.getByRole("link", { name: "Edit as new version", exact: true }).click();
  const toolbar = page.getByRole("region", { name: "Rules grid" });
  const condition = page.getByRole("region", { name: "Condition inspector" });
  const save = page.getByRole("button", { name: "Save version", exact: true });
  await expect(toolbar).toBeVisible();
  markStage("typed-grid-edits");
  await page.getByLabel("Execution mode", { exact: true }).selectOption("allMatching");
  await selectCell(page, 10, "Monthly income");
  await condition.getByLabel("Condition", { exact: true }).selectOption("decimal");
  for (const operation of ["equals", "notEquals", "greaterThan", "greaterThanOrEqual", "lessThan", "lessThanOrEqual"]) {
    await condition.getByLabel("Operator", { exact: true }).selectOption(operation);
    await expect(condition.getByLabel("Operator", { exact: true })).toHaveValue(operation);
  }
  await condition.getByLabel("Operator", { exact: true }).selectOption("greaterThanOrEqual");
  await condition.getByLabel("Decimal value", { exact: true }).fill(exactDecimal);
  await selectCell(page, 10, "Pensioner");
  await condition.getByLabel("Condition", { exact: true }).selectOption("boolean");
  await condition.getByLabel("Boolean value", { exact: true }).selectOption("false");
  await selectCell(page, 10, "Residence country");
  await condition.getByLabel("Condition", { exact: true }).selectOption("stringList");
  await condition.getByLabel("List item", { exact: true }).fill("MLT");
  await condition.getByRole("button", { name: "Add item", exact: true }).click();
  await condition.getByLabel("List item", { exact: true }).fill("ITA");
  await condition.getByLabel("List item", { exact: true }).press("Enter");
  await selectCell(page, 10, "Referral");
  await condition.getByLabel("Condition", { exact: true }).selectOption("any");
  await expect(condition.getByLabel("Operator", { exact: true })).toHaveCount(0);
  await condition.getByLabel("Condition", { exact: true }).selectOption("missing");
  await expect(condition.getByLabel("Text value", { exact: true })).toHaveCount(0);
  await page.getByLabel("State", { exact: true }).selectOption("test");
  await page.getByLabel("Weight", { exact: true }).fill("0.6500");
  await page.getByLabel("Comment", { exact: true }).fill("Portal older draft row");
  const metadataEditor = page.locator('textarea[data-rule-field="metadata"]');
  await metadataEditor.fill(metadata);
  await page.getByLabel("Version note", { exact: true }).fill(saveNote);
  await page.getByRole("button", { name: "Add rule", exact: true }).click();
  await save.click();
  await expect(page.getByRole("alert").filter({ hasText: "Choose a typed condition" })).toBeVisible();
  await page.getByRole("button", { name: "Remove rule", exact: true }).click();
  await selectCell(page, 10, "Monthly income");
  await page.getByRole("button", { name: "Copy rule", exact: true }).click();
  await page.getByRole("button", { name: "Move down", exact: true }).click();
  await page.getByRole("button", { name: "Move up", exact: true }).click();
  await expect(page.getByText("Unsaved changes", { exact: true })).toBeVisible();
  markStage("narrow-grid");
  await screenshot(page, "rm11-native-grid");
  await page.setViewportSize({ width: 1000, height: 1000 });
  await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(1001);
  await expect(page.getByRole("button", { name: "Save version", exact: true })).toBeVisible();
  await screenshot(page, "rm11-native-grid-narrow");
  await page.setViewportSize({ width: 1600, height: 1000 });
  cases.push("rm11-keyboard-membership-decimal-boolean-markers-validation-row-actions-and-narrow-grid");

  markStage("native-competing-save");
  const scope = await read(page, url("context"));
  const other = second.definitionJson.replace(/"mode":"(firstMatch|allMatching)"/u, (_match, mode) => '"mode":"' + (mode === "firstMatch" ? "allMatching" : "firstMatch") + '"');
  assert.notEqual(other, second.definitionJson);
  const competing = await page.request.post(url("versions"), { headers: { origin, "if-match": scope.value.editETag,
    "idempotency-key": randomUUID(), [portalContextHeader]: portalContextKey(scope.context) },
  data: { schemaRevision: scope.value.schemaRevision, definitionJson: other, baseVersion: secondVersion, reason: "Native concurrent editor" } });
  assert.equal(competing.status(), 200);
  const competingReply = await competing.json();
  assert.equal(competingReply.value.disposition, "committed");
  const competingVersion = competingReply.value.version;

  markStage("retained-save-conflict");
  const conflictReply = page.waitForResponse(response => response.url() === url("versions") && response.request().method() === "POST");
  await save.click();
  const nativeConflict = await (await conflictReply).json();
  assert.equal(nativeConflict.state, "ready");
  assert.equal(nativeConflict.value.disposition, "conflict");
  await expect(page.getByRole("heading", { name: "Resolve save conflict" })).toBeVisible();
  await expect(page.getByLabel("Version note", { exact: true })).toHaveValue(saveNote);
  await selectCell(page, 10, "Monthly income");
  await expect(condition.getByLabel("Decimal value", { exact: true })).toHaveValue(exactDecimal);
  await expect(metadataEditor).toHaveValue(metadata);
  await screenshot(page, "rm11-native-save-conflict");
  await page.getByRole("button", { name: "Dismiss original result", exact: true }).click();
  await page.getByRole("button", { name: "Keep draft and use latest revision", exact: true }).click();
  await expect(save).toBeEnabled();

  markStage("original-save-recovery");
  let originalKey, saveVersion, outboundDefinition;
  const saveUrl = url("versions");
  const interrupted = async route => {
    if (route.request().method() !== "POST") { await route.continue(); return; }
    const response = await route.fetch();
    try {
      const accepted = await response.json();
      assert.equal(accepted.state, "ready");
      assert.equal(accepted.value.disposition, "committed");
      originalKey = route.request().headers()["idempotency-key"];
      saveVersion = accepted.value.version;
      outboundDefinition = route.request().postDataJSON().definitionJson;
      await route.abort("failed");
    } finally { await response.dispose(); }
  };
  await page.route(saveUrl, interrupted);
  try {
    await save.click();
    await expect.poll(() => saveVersion).toBeTruthy();
    const check = page.getByRole("button", { name: "Check result", exact: true });
    await expect(check).toBeEnabled();
    assert.equal(await save.isEnabled(), false);
    await check.click();
    await expect(page).toHaveURL(next => next.pathname.endsWith("/versions") && next.searchParams.get("version") === saveVersion);
  } finally { await page.unroute(saveUrl, interrupted); }
  markStage("saved-definition-evidence");
  assert.notEqual(saveVersion, firstVersion);
  const saved = (await read(page, url("version", { version: saveVersion }))).value;
  assert.equal(saved.versionNote, saveNote);
  assert.deepEqual((await read(page, url("deployment"))).value, before);
  assert.equal((await read(page, url("version", { version: firstVersion }))).value.definitionJson, original.definitionJson);
  assert.match(saved.definitionJson, /"value":"9007199254740993[.]000000000001"/u);
  assert.match(saved.definitionJson, /"weight":"0[.]6500"/u);
  assert.ok(saved.definitionJson.includes(metadata));
  assert.ok(outboundDefinition.includes(metadata));
  const definition = JSON.parse(saved.definitionJson);
  const edited = definition.rows.find(row => row.ordinal === 10);
  assert.equal(edited.conditions.find(cell => cell.presetKind === "boolean")?.preset.value, false);
  assert.equal(edited.conditions.find(cell => cell.presetKind === "missing")?.preset, undefined);
  assert.deepEqual(edited.conditions.find(cell => cell.presetKind === "stringList")?.preset.value, ["MLT", "ITA"]);
  assert.equal(definition.mode, "allMatching");
  assert.equal(edited.state, "test");
  cases.push("rm11-native-competing-save-conflict-retains-draft-and-original-key-recovery-with-no-activation");

  markStage("native-deploy-and-lag");
  await page.getByRole("link", { name: "Deploy", exact: true }).click();
  const deployed = await confirm(page, url("deployments"), "Deploy reviewed rule version", "deploy", screenshot, "rm11-native-confirm-deployment");
  assert.equal(deployed.value.version, saveVersion);
  await page.getByRole("link", { name: "Deployment", exact: true }).waitFor();
  await expect(page.getByRole("heading", { name: "Instance adoption", exact: true })).toBeVisible();
  await expect(page.getByText("Pending", { exact: true }).first()).toBeVisible();
  const deployedSummary = (await read(page, url("deployment"))).value;
  assert.equal(deployedSummary.desired.version, saveVersion);
  assert.equal(deployedSummary.requestedAtUtc, deployed.value.requestedAtUtc);
  await page.getByRole("button", { name: "Check result", exact: true }).click();
  await page.getByRole("button", { name: "Dismiss original result", exact: true }).click();

  markStage("native-exact-rollback");
  await page.getByRole("link", { name: "Versions", exact: true }).click();
  await page.getByRole("button", { name: "Initial eligibility", exact: true }).click();
  await page.getByRole("link", { name: "Deploy", exact: true }).click();
  const rollback = await confirm(page, url("deployments"), "Return to the first saved definition", "rollback", screenshot, "rm11-native-confirm-rollback");
  assert.equal(rollback.value.version, firstVersion);
  await expect(page.getByRole("heading", { name: "Instance adoption", exact: true })).toBeVisible();
  const final = (await read(page, url("deployment"))).value;
  assert.equal(final.desired.version, firstVersion);
  assert.equal(final.requestedAtUtc, rollback.value.requestedAtUtc);
  assert.notEqual(final.requestedAtUtc, before.requestedAtUtc);
  cases.push("rm11-native-deploy-original-result-and-exact-rollback-with-real-request-timestamps");

  markStage("separate-read-only-session");
  const blocked = await pageB.request.get(url("version", { version: firstVersion }));
  assert.equal(blocked.status(), 404);
  await pageB.goto(origin + ruleEngineRoute({ applicationId: applicationB, rulesetId, view: "versions", version: readOnlyVersion }));
  await expect(pageB.getByRole("heading", { name: "Saved versions", exact: true })).toBeVisible();
  await expect(pageB.getByText("Read only", { exact: false })).toBeVisible();
  await expect(pageB.getByRole("link", { name: "Deploy", exact: true })).toHaveCount(0);
  await expect(pageB.getByRole("link", { name: "Edit as new version", exact: true })).toHaveCount(0);
  cases.push("rm11-native-read-only-and-cross-application-browser-boundaries");
  return { olderBase: firstVersion, saveVersion, deployedVersion: deployed.value.version, rollbackVersion: rollback.value.version,
    exactDecimal, metadata, competingVersion, originalKey, rejectedObservation: true, pendingObservation: true, appliedObservation: true,
    finalGeneration: final.desired.generation, requestedAtUtc: final.requestedAtUtc };
}
async function selectCell(page, ordinal, column) {
  await page.getByRole("button", { name: new RegExp("^Rule " + ordinal + " " + column + ":") }).click();
}
async function read(page, url) {
  const response = await page.request.get(url);
  assert.equal(response.status(), 200);
  const value = await response.json();
  assert.equal(value.state, "ready");
  return value;
}
async function confirm(page, url, reason, intent, screenshot, name) {
  const dialog = page.getByRole("dialog");
  await expect(dialog).toBeVisible();
  await dialog.getByLabel("Deployment reason", { exact: true }).fill(reason);
  if (await dialog.getByLabel("Intent", { exact: true }).count() > 0) { await dialog.getByLabel("Intent", { exact: true }).selectOption(intent); }
  await screenshot(page, name);
  const submitted = page.waitForResponse(response => response.url() === url && response.request().method() === "POST");
  await dialog.getByRole("button", { name: "Deploy version", exact: true }).click();
  const response = await submitted;
  assert.equal(response.status(), 200);
  assert.equal(response.request().postDataJSON().intent, intent);
  const result = await response.json();
  assert.equal(result.state, "ready");
  assert.equal(result.value.disposition, "committed");
  return result;
}
