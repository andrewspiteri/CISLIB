import assert from "node:assert/strict";
import test from "node:test";
import { applicationSelection, configurationSelection, validRouteParameters } from "../src/page-input.ts";
import { applicationRoute, configurationRoute, jobDetailsRoute, readableSlug } from "../src/portal-routes.ts";

const applicationId = "11111111-1111-1111-1111-111111111111", otherApplication = "22222222-2222-2222-2222-222222222222";
const selected = { applicationId, configurationId: "country.settings", schemaVersion: "9223372036854775807" };
const parse = path => new URL(path, "https://portal.example.test");
const input = url => Object.fromEntries([...new Set(url.searchParams.keys())].map(name => {
  const values = url.searchParams.getAll(name); return [name, values.length === 1 ? values[0] : values];
}));

test("all readable application tabs retain exact native identity and bounded list return context", () => {
  for (const tab of ["instances", "configuration", "capabilities", "jobs"]) {
    const url = parse(applicationRoute({ applicationId, applicationName: "Country Data", tab,
      context: { fromQuery: "Country", fromEnvironment: "Production", fromCursor: "opaque-cursor" } }));
    assert.equal(url.pathname, `/applications/country-data/${tab}`);
    assert.equal(applicationSelection("country-data", input(url)), applicationId);
    assert.equal(url.searchParams.get("fromEnvironment"), "Production");
    assert.equal(url.searchParams.get("fromCursor"), "opaque-cursor");
    assert.equal(url.searchParams.has("environment"), false, "A list filter must not become the selected application's native environment.");
  }
});

test("colliding display names and different environment applications remain distinct without a name resolver", () => {
  const first = parse(applicationRoute({ applicationId, applicationName: "Same! Name", tab: "instances" }));
  const second = parse(applicationRoute({ applicationId: otherApplication, applicationName: "Same Name", tab: "instances" }));
  assert.equal(first.pathname, second.pathname); assert.notEqual(first.search, second.search);
  assert.equal(applicationSelection("an-older-display-name", input(first)), applicationId, "Renaming a cosmetic label cannot break an exact link.");
  assert.equal(applicationSelection("same-name", {}), undefined, "A name alone never selects a resource.");
  const uuidName = parse(applicationRoute({ applicationId, applicationName: otherApplication, tab: "instances" }));
  assert.equal(applicationSelection(uuidName.pathname.split("/")[2], input(uuidName)), applicationId, "UUID-looking display names cannot collide with legacy path selectors.");
});

test("legacy UUID selectors survive while contradictory malformed and duplicate selectors fail closed", () => {
  assert.equal(applicationSelection(applicationId, {}), applicationId);
  assert.equal(applicationSelection(applicationId, { applicationId }), applicationId);
  assert.equal(applicationSelection(applicationId, { applicationId: otherApplication }), undefined);
  for (const bad of [[applicationId, applicationId], "", "not-a-uuid"]) { assert.equal(applicationSelection("country-data", { applicationId: bad }), undefined); }
  for (const path of ["..", "with/slash", "", "x".repeat(81)]) { assert.equal(applicationSelection(path, { applicationId }), undefined); }
  assert.equal(validRouteParameters({ source: ["application", "common"] }, ["source"]), false);
});

test("Configuration detail editor assignment and adoption URLs round trip exact schema and explicit source", () => {
  for (const view of [undefined, "details", "edit", "assignment", "adoption"]) {
    const url = parse(configurationRoute({ ...selected, applicationName: "Country Data", configurationName: "Country Settings", view, source: "common" }));
    assert.equal(url.pathname, "/applications/country-data/configuration/country-settings" + (view === undefined || view === "details" ? "" : `/${view}`));
    assert.deepEqual(configurationSelection("country-data", "older-settings-label", input(url)), selected);
    assert.equal(url.searchParams.get("source"), "common", "Common remains an explicit read selection; routing cannot switch it to application.");
  }
  assert.equal(configurationSelection("country-data", "country-settings", { applicationId, schemaVersion: "1" }), undefined);
  assert.equal(configurationSelection(applicationId, "country.settings", { schemaVersion: "1" }, true)?.configurationId, "country.settings");
  assert.equal(configurationSelection(applicationId, "country.settings", { configurationId: "other.settings", schemaVersion: "1" }, true), undefined);
  const reserved = parse(configurationRoute({ ...selected, configurationId: "new", configurationName: "New" }));
  assert.equal(reserved.pathname, "/applications/application/configuration/configuration-new");
  assert.equal(configurationSelection("application", "configuration-new", input(reserved)).configurationId, "new", "A real ID named new opens details rather than the reserved creation page.");
  for (const schemaVersion of ["0", "01", "9223372036854775808", ["1", "1"]]) {
    assert.equal(configurationSelection("country-data", "settings", { ...selected, schemaVersion }), undefined);
  }
});

test("reported job detail path stays separate from its list and preserves every immutable reported selector", () => {
  const target = { registration: otherApplication, report: "9223372036854775807", scope: "scope / 日本語", job: "job?opaque",
    resource: "resource#1", mode: "Scheduled", cursor: "page-cursor", view: "task" };
  const url = parse(jobDetailsRoute({ applicationId, applicationName: "Country Data", target }));
  assert.equal(url.pathname, "/applications/country-data/jobs/details");
  for (const [key, value] of Object.entries(target)) { assert.equal(url.searchParams.get(key), value); }
  assert.notEqual(url.pathname, parse(applicationRoute({ applicationId, applicationName: "Country Data", tab: "jobs" })).pathname);
});

test("cosmetic slug generation is finite Unicode safe and never lets return context replace a native selector", () => {
  assert.equal(readableSlug(" Café / 日本語 ", "application"), "café-日本語");
  assert.equal(readableSlug("///...", "configuration"), "configuration");
  assert.equal(readableSlug("bad\ud800", "application"), "application");
  assert.equal([...readableSlug("x".repeat(500), "application")].length, 80);
  const url = parse(applicationRoute({ applicationId, tab: "instances", context: { applicationId: otherApplication } }));
  assert.equal(url.searchParams.get("applicationId"), applicationId);
  assert.throws(() => applicationRoute({ applicationId: "name-is-not-authority", tab: "instances" }));
  assert.throws(() => configurationRoute({ ...selected, schemaVersion: "1e3" }));
});
