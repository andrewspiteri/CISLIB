import assert from "node:assert/strict";
import test from "node:test";
import { addRule, moveRule, newRuleCell, parseRuleDefinition, prepareRuleDefinition,
  ruleCellIssue, ruleOperators, updateRuleCell, validRuleDecimal } from "../src/rule-engine-draft.ts";
import { ruleEngineRoute } from "../src/rule-engine-routes.ts";
import { projectRuleOperation } from "../src/rule-engine-operations.ts";

const rulesetId = "fixture-eligibility", schema = "fixture-v1";
const version = "11111111-1111-4111-8111-111111111111";
const app = "22222222-2222-4222-8222-222222222222";
const value = "9007199254740993.000000000001";
const source = '{"rulesetId":"' + rulesetId + '","schemaRevision":"' + schema + '","version":"' + version
  + '","mode":"allMatching","columns":[{"id":"income","displayName":"Monthly income","inputKind":"decimal","allowedPresetKinds":["decimal"]}],'
  + '"rows":[{"ruleId":"r1","ordinal":10,"state":"test","weight":"0.6500","metadata":{"balance":9007199254740993,"rate":1.2300e5},'
  + '"conditions":[{"columnId":"income","presetKind":"decimal","operator":"greaterThanOrEqual","preset":{"kind":"decimal","value":"' + value + '"}}]}]}';

test("rule drafts retain decimal, weight and raw metadata tokens when moving and editing", () => {
  const seed = parseRuleDefinition(source, rulesetId, schema, version);
  assert.equal(seed.rows[0].metadataText, '{"balance":9007199254740993,"rate":1.2300e5}');
  assert.deepEqual(ruleOperators("decimal"), ["equals", "notEquals", "greaterThan", "greaterThanOrEqual", "lessThan", "lessThanOrEqual"]);
  let draft = addRule(seed, "r2", "r1");
  draft = moveRule(draft, "r2", -1);
  draft = updateRuleCell(draft, "r2", newRuleCell("income", "missing"));
  const prepared = prepareRuleDefinition(draft);
  assert.ok(prepared.json);
  assert.match(prepared.json, /"balance":9007199254740993,"rate":1.2300e5/);
  assert.match(prepared.json, /"weight":"0.6500"/);
  const roundTrip = parseRuleDefinition(prepared.json, rulesetId, schema, version);
  assert.equal(roundTrip.rows[1].conditions[0].preset.value, value);
  assert.deepEqual(roundTrip.rows[0].conditions[0], { columnId: "income", presetKind: "missing" });
  assert.deepEqual(roundTrip.rows.map(row => [row.ruleId, row.ordinal]), [["r2", 10], ["r1", 20]]);
  assert.equal(seed.rows[0].ruleId, "r1");
});

test("incomplete ordinary values and unsupported decimal syntax are refused locally", () => {
  assert.equal(validRuleDecimal("79228162514264337593543950335.000"), true);
  assert.equal(validRuleDecimal("79228162514264337593543950336"), false);
  for (const text of ["1e3", "1,000", "00", "+1", "1."]) { assert.equal(validRuleDecimal(text), false, text); }
  const seed = parseRuleDefinition(source, rulesetId, schema, version);
  const draft = addRule(seed, "r2");
  const result = prepareRuleDefinition(draft);
  assert.equal(result.issue?.ruleId, "r2");
  assert.equal(result.issue?.columnId, "income");
  assert.equal(ruleCellIssue(newRuleCell("income", "any"), seed.columns[0]), undefined);
});

test("browser routes keep opaque selectors separate from cosmetic path segments", () => {
  const id = ".. / active+👋/rules#", url = new URL(ruleEngineRoute({ applicationId: app, applicationName: "Country demo",
    rulesetId: id, view: "deploy", version }), "https://portal.example");
  assert.equal(url.searchParams.get("rulesetId"), id);
  assert.equal(url.searchParams.get("applicationId"), app);
  assert.match(url.pathname, /\/country-demo\/rule-engines\/active-rules\/deploy\//);
});

test("operation projection preserves original commitment and refuses cross-target echoes", () => {
  const family = "management.rule-engine.version.save";
  const target = { applicationId: app, rulesetId };
  const known = { ...target, environment: "Dev", operation: { name: family, key: "original", scope: "opaque" },
    originalCompletion: "committed", resultSource: "found", result: { version }, attemptCompletion: { cleanupSucceeded: false } };
  assert.equal(projectRuleOperation({ kind: "http", cleanupSucceeded: false, value: known }, family, target, "original").disposition, "committed");
  assert.equal(projectRuleOperation({ kind: "http", cleanupSucceeded: true, value: { ...known, applicationId: version } },
    family, target, "original").disposition, "unresolved");
});
